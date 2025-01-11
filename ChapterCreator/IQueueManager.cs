using System;
using System.Collections.Generic;
using ChapterCreator.Data;

namespace ChapterCreator;

/// <summary>
/// Interface for managing the queue of library items for analysis.
/// </summary>
public interface IQueueManager
{
    /// <summary>
    /// Gets all media items on the server.
    /// </summary>
    /// <returns>Queued media items.</returns>
    IReadOnlyDictionary<Guid, List<QueuedMedia>> GetMediaItems();

    /// <summary>
    /// Gets media items based on given itemId. Skips all block lists.
    /// </summary>
    /// <param name="itemIds">All item ids to lookup.</param>
    /// <returns>Queued media items.</returns>
    IReadOnlyDictionary<Guid, List<QueuedMedia>> GetMediaItemsById(Guid[] itemIds);

    /// <summary>
    /// Verify that a collection of queued media items still exist in Jellyfin and in storage.
    /// This is done to ensure that we don't use items that were deleted between the call to GetMediaItems() and popping them from the queue.
    /// </summary>
    /// <param name="candidates">Queued media items.</param>
    /// <returns>Media items that have been verified to exist in Jellyfin and in storage.</returns>
    IReadOnlyCollection<QueuedMedia> VerifyQueue(IReadOnlyCollection<QueuedMedia> candidates);
}
