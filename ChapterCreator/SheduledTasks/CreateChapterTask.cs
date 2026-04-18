using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ChapterCreator.Managers;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaSegments;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.MediaSegments;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace ChapterCreator.SheduledTasks;

/// <summary>
/// Create chapter files task.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="CreateChapterTask"/> class.
/// </remarks>
/// <param name="loggerFactory">Logger factory.</param>
/// <param name="libraryManager">Library manager.</param>
/// <param name="mediaSegmentManager">MediaSegment manager.</param>
/// <param name="chapterOutputService">Chapter output service.</param>
public class CreateChapterTask(
    ILoggerFactory loggerFactory,
    ILibraryManager libraryManager,
    IMediaSegmentManager mediaSegmentManager,
    IChapterOutputService chapterOutputService) : IScheduledTask
{
    private readonly ILoggerFactory _loggerFactory = loggerFactory;
    private readonly ILibraryManager _libraryManager = libraryManager;
    private readonly IMediaSegmentManager _mediaSegmentManager = mediaSegmentManager;
    private readonly IChapterOutputService _chapterOutputService = chapterOutputService;

    /// <summary>
    /// Gets the task name.
    /// </summary>
    public string Name => "Create Chapter Markers";

    /// <summary>
    /// Gets the task category.
    /// </summary>
    public string Category => "Intro Skipper";

    /// <summary>
    /// Gets the task description.
    /// </summary>
    public string Description => "Create chapter markers from Media Segments.";

    /// <summary>
    /// Gets the task key.
    /// </summary>
    public string Key => "JFPChapterCreate";

    /// <summary>
    /// Create all chapter files which are not yet created but a media segments is available.
    /// </summary>
    /// <param name="progress">Task progress.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task.</returns>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (_libraryManager is null)
        {
            throw new InvalidOperationException("Library manager was null");
        }

        var baseChapterTask = new BaseChapterTask(_chapterOutputService);

        // Migrate any chapter files from the legacy Jellyfin data folder location.
        Plugin.Instance!.MigrateLegacyChaptersFolderIfNeeded();

        var segmentsList = new List<MediaSegmentDto>();
        // get ItemIds
        var mediaItems = new QueueManager(_loggerFactory.CreateLogger<QueueManager>(), _libraryManager).GetMediaItems();
        // get MediaSegments from itemIds
        foreach (var kvp in mediaItems)
        {
            foreach (var media in kvp.Value)
            {
                var item = Plugin.Instance!.GetItem(media.ItemId);
                if (item is null)
                {
                    continue;
                }

                segmentsList.AddRange(await _mediaSegmentManager.GetSegmentsAsync(item, null, new LibraryOptions(), true).ConfigureAwait(false));
            }
        }

        // write chapter files
        if (segmentsList.Count > 0)
        {
            await baseChapterTask.CreateChaptersAsync(progress, segmentsList, false, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Get task triggers.
    /// </summary>
    /// <returns>Task triggers.</returns>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return [];
    }
}
