using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
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
public class CreateChapterTask(
    ILoggerFactory loggerFactory,
    ILibraryManager libraryManager,
    IMediaSegmentManager mediaSegmentManager) : IScheduledTask
{
    private readonly ILoggerFactory _loggerFactory = loggerFactory;

    private readonly ILibraryManager _libraryManager = libraryManager;

    private readonly IMediaSegmentManager _mediaSegmentManager = mediaSegmentManager;

    /// <summary>
    /// Gets the task name.
    /// </summary>
    public string Name => "Create Chapters";

    /// <summary>
    /// Gets the task category.
    /// </summary>
    public string Category => "Intro Skipper";

    /// <summary>
    /// Gets the task description.
    /// </summary>
    public string Description => "Create chapter xml files from Media Segments.";

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

        var baseChapterTask = new BaseChapterTask(
            _loggerFactory.CreateLogger<CreateChapterTask>());

        var queueManager = new QueueManager(_loggerFactory.CreateLogger<QueueManager>(), _libraryManager);

        var segmentsList = new List<MediaSegmentDto>();
        // get ItemIds
        var mediaItems = queueManager.GetMediaItems();
        // get MediaSegments from itemIds
        foreach (var kvp in mediaItems)
        {
            foreach (var media in kvp.Value)
            {
                segmentsList.AddRange(await _mediaSegmentManager.GetSegmentsAsync(media.ItemId, null, true).ConfigureAwait(false));
            }
        }

        // write chapter files
        if (segmentsList.Count > 0)
        {
            baseChapterTask.CreateChapters(progress, segmentsList.AsReadOnly(), false, cancellationToken);
        }

        return;
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
