using System;
using System.Collections.Generic;
using ChapterCreator.Data;
using MediaBrowser.Model.MediaSegments;

namespace ChapterCreator;

/// <summary>
/// Interface for chapter management operations.
/// </summary>
public interface IChapterManager
{
    /// <summary>
    /// Logs the configuration that will be used during Chapter file creation.
    /// </summary>
    void LogConfiguration();

    /// <summary>
    /// Update Chapter file for the provided segments.
    /// </summary>
    /// <param name="psegment">Key value pair of segments dictionary.</param>
    /// <param name="forceOverwrite">Force the file overwrite.</param>
    void UpdateChapterFile(KeyValuePair<Guid, List<MediaSegmentDto>> psegment, bool forceOverwrite);

    /// <summary>
    /// Convert segments to a Kodi compatible Chapter entry.
    /// </summary>
    /// <param name="id">The ItemId.</param>
    /// <param name="segments">The Segments.</param>
    /// <returns>String content of chapter file.</returns>
    IReadOnlyList<Chapter> ToChapter(Guid id, IReadOnlyCollection<MediaSegmentDto> segments);
}
