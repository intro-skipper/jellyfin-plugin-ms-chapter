using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.MediaSegments;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ChapterCreator.SheduledTasks
{
    /// <summary>
    /// Common code shared by all chapter creator tasks.
    /// </summary>
    public class BaseChapterTask
    {
            private readonly ILogger _logger;

            /// <summary>
            /// Initializes a new instance of the <see cref="BaseChapterTask"/> class.
            /// </summary>
            /// <param name="logger">Task logger.</param>
            public BaseChapterTask(
                ILogger logger)
            {
                _logger = logger;

                ChapterManager.Initialize(_logger);
            }

            /// <summary>
            /// Create chapters for all Segments on the server.
            /// </summary>
            /// <param name="progress">Progress.</param>
            /// <param name="segmentsQueue">Media segments.</param>
            /// <param name="forceOverwrite">Force the file overwrite.</param>
            /// <param name="cancellationToken">Cancellation token.</param>
            public void CreateChapters(
                IProgress<double> progress,
                ReadOnlyCollection<MediaSegmentDto> segmentsQueue,
                bool forceOverwrite,
                CancellationToken cancellationToken)
            {
                // Group segments by ItemId and sort them by StartTicks in one pass
                var sortedSegments = segmentsQueue
                    .GroupBy(s => s.ItemId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.OrderBy(s => s.StartTicks).ToList())
                    .OrderBy(kvp => kvp.Key)
                    .ToList();

                var totalQueued = sortedSegments.Count;

                ChapterManager.LogConfiguration();

                var totalProcessed = 0;
                var options = new ParallelOptions()
                {
                    MaxDegreeOfParallelism = Plugin.Instance!.Configuration.MaxParallelism
                };

                Parallel.ForEach(sortedSegments, options, (segment) =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    ChapterManager.UpdateChapterFile(segment, forceOverwrite);
                    Interlocked.Add(ref totalProcessed, 1);

                    progress.Report(totalProcessed * 100 / totalQueued);
            });
        }
    }
}
