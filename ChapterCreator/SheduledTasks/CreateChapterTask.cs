using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ChapterCreator.Data;
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
/// <param name="libraryManager">Library manager.</param>
/// <param name="mediaSegmentManager">MediaSegment manager.</param>
/// <param name="legacyChapterMigrator">Legacy chapter migrator.</param>
/// <param name="queueManager">Queue manager.</param>
/// <param name="chapterTaskRunner">Chapter task runner.</param>
/// <param name="logger">Logger.</param>
public partial class CreateChapterTask(
    ILibraryManager libraryManager,
    IMediaSegmentManager mediaSegmentManager,
    ILegacyChapterMigrator legacyChapterMigrator,
    IQueueManager queueManager,
    IChapterTaskRunner chapterTaskRunner,
    ILogger<CreateChapterTask> logger) : IScheduledTask
{
    private readonly ILibraryManager _libraryManager = libraryManager;
    private readonly IMediaSegmentManager _mediaSegmentManager = mediaSegmentManager;
    private readonly ILegacyChapterMigrator _legacyChapterMigrator = legacyChapterMigrator;
    private readonly IQueueManager _queueManager = queueManager;
    private readonly IChapterTaskRunner _chapterTaskRunner = chapterTaskRunner;
    private readonly ILogger<CreateChapterTask> _logger = logger;

    /// <summary>
    /// Gets the task name.
    /// </summary>
    public string Name => "Generate Chapter Markers";

    /// <summary>
    /// Gets the task category.
    /// </summary>
    public string Category => "Intro Skipper";

    /// <summary>
    /// Gets the task description.
    /// </summary>
    public string Description => "Write media segments as chapter markers to XML or the Jellyfin database.";

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
        // Migrate any chapter files from the legacy Jellyfin data folder location.
        _legacyChapterMigrator.MigrateIfNeeded();

        var segmentsList = new List<MediaSegmentDto>();
        foreach (var media in _queueManager.GetMediaItems())
        {
            var item = _libraryManager.GetItemById(media.ItemId);
            if (item is null)
            {
                continue;
            }

            IEnumerable<MediaSegmentDto> itemSegments;
            try
            {
                itemSegments = await _mediaSegmentManager.GetSegmentsAsync(
                    item, null, new LibraryOptions(), true).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogRetrieveMediaSegmentsFailure(_logger, media.ItemId, ex);
                continue;
            }

            segmentsList.AddRange(itemSegments);
        }

        if (segmentsList.Count > 0)
        {
            await _chapterTaskRunner.CreateChaptersAsync(progress, segmentsList, false, cancellationToken).ConfigureAwait(false);
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

    [LoggerMessage(EventId = 1000, Level = LogLevel.Error, Message = "Failed to retrieve media segments for item {Id}, skipping")]
    private static partial void LogRetrieveMediaSegmentsFailure(ILogger logger, Guid id, Exception ex);
}
