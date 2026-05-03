namespace ChapterCreator.Managers;

/// <summary>
/// Migrates chapter files from the legacy centralized introskipper data folder
/// to per-media locations next to each media file.
/// </summary>
public interface ILegacyChapterMigrator
{
    /// <summary>
    /// Migrates legacy chapter files if the legacy folder exists.
    /// Files are moved from the centralized data folder to directories
    /// adjacent to their corresponding media files.
    /// </summary>
    void MigrateIfNeeded();
}
