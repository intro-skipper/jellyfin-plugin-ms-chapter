using System;
using System.IO;
using ChapterCreator.Data;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace ChapterCreator.Managers;

/// <summary>
/// Migrates chapter files from the legacy centralized introskipper data folder
/// to per-media locations next to each media file.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="LegacyChapterMigrator"/> class.
/// </remarks>
/// <param name="applicationPaths">The application paths.</param>
/// <param name="libraryManager">The library manager.</param>
/// <param name="logger">The logger instance.</param>
public partial class LegacyChapterMigrator(
    IApplicationPaths applicationPaths,
    ILibraryManager libraryManager,
    ILogger<LegacyChapterMigrator> logger) : ILegacyChapterMigrator
{
    private const string IntroSkipperDataDir = "introskipper";
    private const string ChaptersDir = Constants.LegacyChaptersDirectory;

    private readonly IApplicationPaths _applicationPaths = applicationPaths;
    private readonly ILibraryManager _libraryManager = libraryManager;
    private readonly ILogger<LegacyChapterMigrator> _logger = logger;

    /// <summary>
    /// Gets the path to the legacy centralized chapters folder inside the introskipper data directory.
    /// </summary>
    private string LegacyChaptersFolderPath => Path.Join(_applicationPaths.DataPath, IntroSkipperDataDir, ChaptersDir);

    /// <inheritdoc />
    public void MigrateIfNeeded()
    {
        if (!Directory.Exists(LegacyChaptersFolderPath))
        {
            return;
        }

        LogMigratingLegacyChapters(_logger, LegacyChaptersFolderPath);

        foreach (var file in Directory.GetFiles(LegacyChaptersFolderPath, $"*{Constants.ChapterFileSuffix}.xml"))
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            if (stem.Length <= Constants.ChapterFileSuffix.Length || !stem.EndsWith(Constants.ChapterFileSuffix, StringComparison.Ordinal))
            {
                LogSkippingUnrecognisedFile(_logger, file);
                continue;
            }

            var idStr = stem[..^Constants.ChapterFileSuffix.Length];

            if (!Guid.TryParse(idStr, out var id))
            {
                LogSkippingUnrecognisedFile(_logger, file);
                continue;
            }

            var item = id != Guid.Empty ? _libraryManager.GetItemById(id) : null;
            if (item is null || string.IsNullOrEmpty(item.Path))
            {
                LogNoItemForChapterFile(_logger, file);
                continue;
            }

            string realPath;
            try
            {
                realPath = File.ResolveLinkTarget(item.Path, returnFinalTarget: true)?.FullName ?? item.Path;
            }
            catch (Exception ex)
            {
                LogSymlinkResolveFailure(_logger, id, item.Path, ex);
                continue;
            }

            var dir = Path.GetDirectoryName(realPath);
            if (string.IsNullOrEmpty(dir))
            {
                LogCannotDetermineDirectory(_logger, id, realPath);
                continue;
            }

            var newPath = Path.Combine(dir, Constants.ChaptersDirectory, $"{Path.GetFileNameWithoutExtension(realPath)}{Constants.ChapterFileSuffix}.xml");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
                if (File.Exists(newPath))
                {
                    LogOverwritingChapterFile(_logger, newPath);
                }

                File.Move(file, newPath, overwrite: true);
                LogMovedChapterFile(_logger, id, file, newPath);
            }
            catch (Exception ex)
            {
                LogMoveChapterFileFailure(_logger, file, ex);
            }
        }

        TryRemoveLegacyFolders();
    }

    private void TryRemoveLegacyFolders()
    {
        try
        {
            if (Directory.Exists(LegacyChaptersFolderPath) && Directory.GetFileSystemEntries(LegacyChaptersFolderPath).Length == 0)
            {
                Directory.Delete(LegacyChaptersFolderPath);
                LogRemovedLegacyFolder(_logger, LegacyChaptersFolderPath);
            }
        }
        catch (Exception ex)
        {
            LogRemoveLegacyFolderFailure(_logger, LegacyChaptersFolderPath, ex);
        }

        var introSkipperPath = Path.Join(_applicationPaths.DataPath, IntroSkipperDataDir);
        try
        {
            if (Directory.Exists(introSkipperPath) && Directory.GetFileSystemEntries(introSkipperPath).Length == 0)
            {
                Directory.Delete(introSkipperPath);
                LogRemovedLegacyFolder(_logger, introSkipperPath);
            }
        }
        catch (Exception ex)
        {
            LogRemoveLegacyFolderFailure(_logger, introSkipperPath, ex);
        }
    }

    [LoggerMessage(EventId = 1200, Level = LogLevel.Information, Message = "Migrating legacy chapter files from {Path}")]
    private static partial void LogMigratingLegacyChapters(ILogger logger, string path);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Debug, Message = "Skipping unrecognised chapter file in chapters folder: {File}")]
    private static partial void LogSkippingUnrecognisedFile(ILogger logger, string file);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Debug, Message = "No library item found for chapter file {File}, leaving in legacy folder")]
    private static partial void LogNoItemForChapterFile(ILogger logger, string file);

    [LoggerMessage(EventId = 1203, Level = LogLevel.Warning, Message = "Could not resolve symlink for item {Id} at {Path}, skipping")]
    private static partial void LogSymlinkResolveFailure(ILogger logger, Guid id, string path, Exception ex);

    [LoggerMessage(EventId = 1204, Level = LogLevel.Warning, Message = "Could not determine directory for item {Id} at {Path}, skipping")]
    private static partial void LogCannotDetermineDirectory(ILogger logger, Guid id, string path);

    [LoggerMessage(EventId = 1205, Level = LogLevel.Debug, Message = "Overwriting existing chapter file at destination: {NewPath}")]
    private static partial void LogOverwritingChapterFile(ILogger logger, string newPath);

    [LoggerMessage(EventId = 1206, Level = LogLevel.Debug, Message = "Moved chapter file for item {Id}: {OldPath} -> {NewPath}")]
    private static partial void LogMovedChapterFile(ILogger logger, Guid id, string oldPath, string newPath);

    [LoggerMessage(EventId = 1207, Level = LogLevel.Warning, Message = "Could not move chapter file {File} back next to media")]
    private static partial void LogMoveChapterFileFailure(ILogger logger, string file, Exception ex);

    [LoggerMessage(EventId = 1208, Level = LogLevel.Information, Message = "Removed empty legacy folder {Path}")]
    private static partial void LogRemovedLegacyFolder(ILogger logger, string path);

    [LoggerMessage(EventId = 1209, Level = LogLevel.Debug, Message = "Could not remove legacy folder {Path}")]
    private static partial void LogRemoveLegacyFolderFailure(ILogger logger, string path, Exception ex);
}
