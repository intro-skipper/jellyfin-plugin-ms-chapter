using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ChapterCreator.Configuration;
using ChapterCreator.Managers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using Microsoft.Extensions.Logging;

namespace ChapterCreator.Providers;

/// <summary>
/// Provides chapter information for media items during library scans.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ChapterProvider"/> class.
/// </remarks>
/// <param name="chapterOutputService">The chapter output service.</param>
/// <param name="mediaSegmentManager">The media segment manager.</param>
/// <param name="logger">The logger instance.</param>
/// <param name="configurationAccessor">The plugin configuration accessor.</param>
public partial class ChapterProvider(
    IChapterOutputService chapterOutputService,
    IMediaSegmentManager mediaSegmentManager,
    ILogger<ChapterProvider> logger,
    IPluginConfigurationAccessor configurationAccessor) : ICustomMetadataProvider<Episode>,
    ICustomMetadataProvider<Movie>,
    IHasItemChangeMonitor,
    IHasOrder
{
    private readonly IChapterOutputService _chapterOutputService = chapterOutputService;
    private readonly IMediaSegmentManager _mediaSegmentManager = mediaSegmentManager;
    private readonly ILogger<ChapterProvider> _logger = logger;
    private readonly IPluginConfigurationAccessor _configurationAccessor = configurationAccessor;

    /// <inheritdoc />
    public string Name => "Chapter Provider";

    /// <inheritdoc />
    public int Order => 100;

    /// <inheritdoc />
    public bool HasChanged(BaseItem item, IDirectoryService directoryService)
    {
        if (!item.IsFileProtocol || string.IsNullOrWhiteSpace(item.Path))
        {
            return false;
        }

        var file = directoryService.GetFile(item.Path);
        if (file is not null && file.LastWriteTimeUtc > item.DateLastSaved)
        {
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public Task<ItemUpdateType> FetchAsync(Episode item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        return FetchInternal(item, options, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ItemUpdateType> FetchAsync(Movie item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        return FetchInternal(item, options, cancellationToken);
    }

    private async Task<ItemUpdateType> FetchInternal(Video video, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        var config = _configurationAccessor.GetConfiguration();
        var segmentsList = await _mediaSegmentManager.GetSegmentsAsync(
            video,
            null,
            new LibraryOptions(),
            true).ConfigureAwait(false);

        if (!segmentsList.Any())
        {
            if (config.ImportXmlChapters)
            {
                try
                {
                    await _chapterOutputService.ImportFromXmlAsync(video.Id, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogXmlImportFailure(_logger, video.Id, video.Name, ex);
                }
            }

            return ItemUpdateType.None;
        }

        if (!config.AutoRefresh)
        {
            return ItemUpdateType.None;
        }

        var sortedSegments = segmentsList.SortForItem(video.Id);

        try
        {
            await _chapterOutputService.ProcessChaptersAsync(sortedSegments, false, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogProcessChaptersFailure(_logger, video.Id, video.Name, ex);
        }

        return ItemUpdateType.None;
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Error, Message = "Failed to import XML chapters for item {Id} ({Name}), skipping")]
    private static partial void LogXmlImportFailure(ILogger logger, Guid id, string? name, Exception ex);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Error, Message = "Failed to process chapters for item {Id} ({Name}), skipping")]
    private static partial void LogProcessChaptersFailure(ILogger logger, Guid id, string? name, Exception ex);
}
