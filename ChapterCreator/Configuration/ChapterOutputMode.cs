namespace ChapterCreator.Configuration;

/// <summary>
/// Defines how chapter data is output.
/// </summary>
public enum ChapterOutputMode
{
    /// <summary>
    /// Write Matroska XML chapter files only.
    /// </summary>
    XmlOnly,

    /// <summary>
    /// Inject chapters into Jellyfin's internal database only.
    /// </summary>
    InjectOnly,

    /// <summary>
    /// Write XML files and inject into Jellyfin's database.
    /// </summary>
    Both
}
