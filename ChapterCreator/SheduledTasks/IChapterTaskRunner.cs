using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.MediaSegments;

namespace ChapterCreator.SheduledTasks;

/// <summary>
/// Executes chapter generation for a set of media segments.
/// </summary>
public interface IChapterTaskRunner
{
    /// <summary>
    /// Creates chapters for the supplied media segments.
    /// </summary>
    /// <param name="progress">The progress reporter.</param>
    /// <param name="segmentsQueue">The segments to process.</param>
    /// <param name="forceOverwrite">Whether existing chapter data should be overwritten.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task CreateChaptersAsync(
        IProgress<double> progress,
        IReadOnlyCollection<MediaSegmentDto> segmentsQueue,
        bool forceOverwrite,
        CancellationToken cancellationToken);
}
