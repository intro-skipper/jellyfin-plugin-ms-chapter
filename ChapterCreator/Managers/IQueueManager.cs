using System.Collections.Generic;
using ChapterCreator.Data;

namespace ChapterCreator.Managers;

/// <summary>
/// Enumerates media items that should be processed for chapter generation.
/// </summary>
public interface IQueueManager
{
    /// <summary>
    /// Gets the ordered media items selected for analysis.
    /// </summary>
    /// <returns>An ordered read-only list of queued media items.</returns>
    IReadOnlyList<QueuedMedia> GetMediaItems();
}
