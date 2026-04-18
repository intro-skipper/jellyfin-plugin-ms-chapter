using System;
using System.Collections.Generic;
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
/// <param name="chapterOutputService">Chapter output service.</param>
public class BaseChapterTask(IChapterOutputService chapterOutputService)
{
    private readonly IChapterOutputService _chapterOutputService = chapterOutputService;

    /// <summary>
    /// Create chapters for all segments on the server.
    /// </summary>
    /// <param name="progress">Progress.</param>
    /// <param name="segmentsQueue">Media segments.</param>
    /// <param name="forceOverwrite">Force the file overwrite.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task CreateChaptersAsync(
        IProgress<double> progress,
        IReadOnlyCollection<MediaSegmentDto> segmentsQueue,
        bool forceOverwrite,
        CancellationToken cancellationToken)
    {
        _chapterOutputService.LogConfiguration();

        // Group segments by ItemId and sort them by StartTicks in one pass
        var sortedSegments = segmentsQueue.GroupAndSortByItem();

        var totalQueued = sortedSegments.Count;
        var totalProcessed = 0;
        var maxParallelism = Plugin.Instance!.Configuration.MaxParallelism;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxParallelism,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(sortedSegments, options, async (segment, ct) =>
        {
            await _chapterOutputService.ProcessChaptersAsync(segment, forceOverwrite, ct).ConfigureAwait(false);
            var processed = Interlocked.Increment(ref totalProcessed);
            progress.Report(processed * 100.0 / totalQueued);
        }).ConfigureAwait(false);
    }
}
