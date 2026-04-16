using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.MediaSegments;

namespace ChapterCreator.Managers;

/// <summary>
/// Interface for unified chapter output operations.
/// Routes chapter data to XML files and/or Jellyfin's internal database
/// based on plugin configuration.
/// </summary>
public interface IChapterOutputService
{
    /// <summary>
    /// Logs the current chapter output configuration for diagnostics.
    /// </summary>
    void LogConfiguration();

    /// <summary>
    /// Processes chapter output for a single media item.
    /// Writes XML files, injects into Jellyfin DB, or both, based on configuration.
    /// </summary>
    /// <param name="segments">Key value pair of item ID to its media segments.</param>
    /// <param name="forceOverwrite">Force overwrite of existing chapter data.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="ValueTask"/> representing the synchronous operation.</returns>
    ValueTask ProcessChaptersAsync(
        KeyValuePair<Guid, List<MediaSegmentDto>> segments,
        bool forceOverwrite,
        CancellationToken cancellationToken);

    /// <summary>
    /// Imports chapters from an existing XML chapter file into Jellyfin's database.
    /// Used when no media segments exist but an XML file is present.
    /// </summary>
    /// <param name="itemId">The media item ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="ValueTask"/> representing the synchronous operation.</returns>
    ValueTask ImportFromXmlAsync(Guid itemId, CancellationToken cancellationToken);
}
