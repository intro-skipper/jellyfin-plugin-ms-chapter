using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ChapterCreator.Managers;
using MediaBrowser.Model.MediaSegments;

namespace ChapterCreator.SheduledTasks;

/// <summary>
/// Common code shared by all chapter creator tasks.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="BaseChapterTask"/> class.
/// </remarks>
/// <param name="chapterManager">ChapterManager.</param>
public class BaseChapterTask(IChapterManager chapterManager)
{
    private readonly IChapterManager _chapterManager = chapterManager;

    /// <summary>
    /// Create chapters for all Segments on the server.
    /// </summary>
    /// <param name="progress">Progress.</param>
    /// <param name="segmentsQueue">Media segments.</param>
    /// <param name="forceOverwrite">Force the file overwrite.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public void CreateChapters(
        IProgress<double> progress,
        IReadOnlyCollection<MediaSegmentDto> segmentsQueue,
        bool forceOverwrite,
        CancellationToken cancellationToken)
    {
        // Group segments by ItemId and sort them by StartTicks in one pass
        var sortedSegments = segmentsQueue
            .GroupBy(s => s.ItemId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(s => s.StartTicks).ToList());

        var totalQueued = sortedSegments.Count;

        _chapterManager.LogConfiguration();

        var totalProcessed = 0;
        var options = new ParallelOptions()
        {
            MaxDegreeOfParallelism = Plugin.Instance!.Configuration.MaxParallelism
        };

        Parallel.ForEach(sortedSegments, options, (segment) =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            _chapterManager.UpdateChapterFile(segment, forceOverwrite);
            Interlocked.Add(ref totalProcessed, 1);

            progress.Report(totalProcessed * 100 / totalQueued);
    });
    }
}
