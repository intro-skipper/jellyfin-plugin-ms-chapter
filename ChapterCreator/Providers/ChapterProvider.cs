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

namespace ChapterCreator.Providers;

/// <summary>
/// Class ChapterProvider. Provides chapter information
/// for media items during library scans.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ChapterProvider"/> class.
/// </remarks>
/// <param name="chapterOutputService">The chapter output service.</param>
/// <param name="mediaSegmentManager">The media segment manager.</param>
public class ChapterProvider(
    IChapterOutputService chapterOutputService,
    IMediaSegmentManager mediaSegmentManager) : ICustomMetadataProvider<Episode>,
    ICustomMetadataProvider<Movie>,
    IHasItemChangeMonitor,
    IHasOrder
{
    private readonly IChapterOutputService _chapterOutputService = chapterOutputService;
    private readonly IMediaSegmentManager _mediaSegmentManager = mediaSegmentManager;

    /// <inheritdoc />
    public string Name => "Chapter Provider";

    /// <inheritdoc />
    public int Order => 100;

    /// <inheritdoc />
    public bool HasChanged(BaseItem item, IDirectoryService directoryService)
    {
        if (item.IsFileProtocol)
        {
            var file = directoryService.GetFile(item.Path);
            if (file is not null && item.HasChanged(file.LastWriteTimeUtc))
            {
                return true;
            }
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
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        if (!config.AutoRefresh)
        {
            return ItemUpdateType.None;
        }

        var segmentsList = await _mediaSegmentManager.GetSegmentsAsync(
            video,
            null,
            new LibraryOptions(),
            true).ConfigureAwait(false);

        if (!segmentsList.Any())
        {
            // No segments found — try importing from existing XML file
            await _chapterOutputService.ImportFromXmlAsync(video.Id, cancellationToken).ConfigureAwait(false);
            return ItemUpdateType.None;
        }

        var sortedSegments = segmentsList.SortForItem(video.Id);

        await _chapterOutputService.ProcessChaptersAsync(sortedSegments, false, cancellationToken).ConfigureAwait(false);

        return ItemUpdateType.None;
    }
}
