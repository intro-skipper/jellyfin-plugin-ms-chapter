using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Model.MediaSegments;

namespace ChapterCreator.Managers;

/// <summary>
/// Extension methods for grouping and sorting <see cref="MediaSegmentDto"/> collections.
/// </summary>
internal static class MediaSegmentExtensions
{
    /// <summary>
    /// Groups segments by <see cref="MediaSegmentDto.ItemId"/> and sorts each
    /// group by <see cref="MediaSegmentDto.StartTicks"/> in ascending order.
    /// </summary>
    /// <param name="segments">The flat list of media segments.</param>
    /// <returns>A dictionary keyed by item ID with sorted segment lists.</returns>
    public static Dictionary<Guid, List<MediaSegmentDto>> GroupAndSortByItem(
        this IEnumerable<MediaSegmentDto> segments)
    {
        return segments
            .GroupBy(s => s.ItemId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(s => s.StartTicks).ToList());
    }

    /// <summary>
    /// Creates a single <see cref="KeyValuePair{Guid, List}"/> for one item,
    /// sorting the segments by <see cref="MediaSegmentDto.StartTicks"/>.
    /// </summary>
    /// <param name="segments">The segments belonging to a single item.</param>
    /// <param name="itemId">The item ID.</param>
    /// <returns>A key-value pair of item ID to sorted segment list.</returns>
    public static KeyValuePair<Guid, List<MediaSegmentDto>> SortForItem(
        this IEnumerable<MediaSegmentDto> segments,
        Guid itemId)
    {
        return new KeyValuePair<Guid, List<MediaSegmentDto>>(
            itemId,
            [.. segments.OrderBy(s => s.StartTicks)]);
    }
}
