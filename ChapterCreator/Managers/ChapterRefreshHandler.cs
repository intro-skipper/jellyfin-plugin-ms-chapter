using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ChapterCreator.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Model.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChapterCreator.Managers;

/// <summary>
/// Restores injected chapter data after Jellyfin imports chapters from a media file.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ChapterRefreshHandler"/> class.
/// </remarks>
/// <param name="libraryManager">Library manager.</param>
/// <param name="mediaSegmentManager">Media segment manager.</param>
/// <param name="chapterOutputService">Chapter output service.</param>
/// <param name="configurationAccessor">Plugin configuration accessor.</param>
/// <param name="logger">Logger.</param>
public sealed partial class ChapterRefreshHandler(
    ILibraryManager libraryManager,
    IMediaSegmentManager mediaSegmentManager,
    IChapterOutputService chapterOutputService,
    IPluginConfigurationAccessor configurationAccessor,
    ILogger<ChapterRefreshHandler> logger) : IHostedService
{
    private readonly ILibraryManager _libraryManager = libraryManager;
    private readonly IMediaSegmentManager _mediaSegmentManager = mediaSegmentManager;
    private readonly IChapterOutputService _chapterOutputService = chapterOutputService;
    private readonly IPluginConfigurationAccessor _configurationAccessor = configurationAccessor;
    private readonly ILogger<ChapterRefreshHandler> _logger = logger;
    private readonly ConcurrentDictionary<Guid, byte> _itemsBeingReinjected = new();
    private readonly CancellationTokenSource _shutdown = new();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemUpdated += OnItemUpdated;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemUpdated -= OnItemUpdated;
        _shutdown.Cancel();
        _shutdown.Dispose();
        return Task.CompletedTask;
    }

    private void OnItemUpdated(object? sender, ItemChangeEventArgs args)
    {
        var config = _configurationAccessor.GetConfiguration();
        if (config.SkipEmbeddedChapters
            || config.OutputMode is not (ChapterOutputMode.InjectOnly or ChapterOutputMode.Both)
            || args.UpdateReason != ItemUpdateType.MetadataImport
            || args.Item is not (Episode or Movie)
            || !_itemsBeingReinjected.TryAdd(args.Item.Id, 0))
        {
            return;
        }

        _ = ReinjectAsync(args.Item, _shutdown.Token);
    }

    private async Task ReinjectAsync(BaseItem item, CancellationToken cancellationToken)
    {
        try
        {
            var segments = await _mediaSegmentManager.GetSegmentsAsync(
                item,
                null,
                new LibraryOptions(),
                true).ConfigureAwait(false);

            if (segments.Count == 0)
            {
                return;
            }

            await _chapterOutputService.InjectChaptersAsync(
                segments.SortForItem(item.Id),
                cancellationToken).ConfigureAwait(false);
            LogReinjectedChapters(_logger, item.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            LogReinjectionFailure(_logger, item.Id, ex);
        }
        finally
        {
            _itemsBeingReinjected.TryRemove(item.Id, out _);
        }
    }

    [LoggerMessage(EventId = 1300, Level = LogLevel.Debug, Message = "Re-injected chapters after metadata refresh for item {Id}")]
    private static partial void LogReinjectedChapters(ILogger logger, Guid id);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Warning, Message = "Failed to re-inject chapters after metadata refresh for item {Id}")]
    private static partial void LogReinjectionFailure(ILogger logger, Guid id, Exception ex);
}
