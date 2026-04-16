using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ChapterCreator.Configuration;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace ChapterCreator
{
    /// <summary>
    /// The main plugin.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        private const string IntroSkipperDataDir = "introskipper";
        private const string ChaptersDir = "chapters";

        private readonly ILibraryManager _libraryManager;
        private readonly IChapterRepository _chapterRepository;
        private readonly ILogger<Plugin> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="Plugin"/> class.
        /// </summary>
        /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
        /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="chapterRepository">Chapter repository.</param>
        /// <param name="logger">Logger.</param>
        public Plugin(
            IApplicationPaths applicationPaths,
            IXmlSerializer xmlSerializer,
            ILibraryManager libraryManager,
            IChapterRepository chapterRepository,
            ILogger<Plugin> logger)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;

            _libraryManager = libraryManager;
            _chapterRepository = chapterRepository;
            _logger = logger;
        }

        /// <inheritdoc />
        public override string Name => "Chapter Creator";

        /// <inheritdoc />
        public override Guid Id => Guid.Parse("e22fb8f5-bc98-4f76-9be4-87de302a97ea");

        /// <summary>
        /// Gets the current plugin instance.
        /// </summary>
        public static Plugin? Instance { get; private set; }

        /// <summary>
        /// Gets the path to the legacy centralized chapters folder inside the introskipper data directory.
        /// </summary>
        public string LegacyChaptersFolderPath => Path.Join(ApplicationPaths.DataPath, IntroSkipperDataDir, ChaptersDir);

        /// <inheritdoc />
        public IEnumerable<PluginPageInfo> GetPages()
        {
            return
            [
                new PluginPageInfo
                {
                    Name = Name,
                    EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
                },
            ];
        }

        /// <inheritdoc />
        public override void UpdateConfiguration(BasePluginConfiguration configuration)
        {
            base.UpdateConfiguration(configuration);
        }

        internal BaseItem? GetItem(Guid id)
        {
            return id != Guid.Empty ? _libraryManager.GetItemById(id) : null;
        }

        /// <summary>
        /// Gets the full path for an item.
        /// </summary>
        /// <param name="id">Item id.</param>
        /// <returns>Full path to item.</returns>
        internal string GetItemPath(Guid id)
        {
            return GetItem(id)?.Path ?? string.Empty;
        }

        /// <summary>
        /// Gets all chapters for this item.
        /// </summary>
        /// <param name="id">Item id.</param>
        /// <returns>List of chapters.</returns>
        internal IReadOnlyList<ChapterInfo> GetChapters(Guid id) => _chapterRepository.GetChapters(id);

        internal void MigrateLegacyChaptersFolderIfNeeded()
        {
            if (!Directory.Exists(LegacyChaptersFolderPath))
            {
                return;
            }

            _logger.LogInformation("Migrating legacy chapter files from {Path}", LegacyChaptersFolderPath);

            foreach (var file in Directory.GetFiles(LegacyChaptersFolderPath, $"*{Constants.ChapterFileSuffix}.xml"))
            {
                var stem = Path.GetFileNameWithoutExtension(file); // "{guid}_chapters"
                var idStr = stem[..^Constants.ChapterFileSuffix.Length];

                if (!Guid.TryParse(idStr, out var id))
                {
                    _logger.LogDebug("Skipping unrecognised chapter file in chapters folder: {File}", file);
                    continue;
                }

                var item = GetItem(id);
                if (item is null || string.IsNullOrEmpty(item.Path))
                {
                    _logger.LogDebug("No library item found for chapter file {File}, leaving in legacy folder", file);
                    continue;
                }

                // Resolve any VFS symlink so the chapter file lands next to the real media.
                string realPath;
                try
                {
                    realPath = File.ResolveLinkTarget(item.Path, returnFinalTarget: true)?.FullName ?? item.Path;
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Could not resolve symlink for item {Id} at {Path}, skipping", id, item.Path);
                    continue;
                }

                var dir = Path.GetDirectoryName(realPath);
                if (string.IsNullOrEmpty(dir))
                {
                    _logger.LogWarning("Could not determine directory for item {Id} at {Path}, skipping", id, realPath);
                    continue;
                }

                var newPath = Path.Combine(dir, ChaptersDir, $"{Path.GetFileNameWithoutExtension(realPath)}{Constants.ChapterFileSuffix}.xml");

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
                    if (File.Exists(newPath))
                    {
                        _logger.LogDebug("Overwriting existing chapter file at destination: {New}", newPath);
                    }

                    File.Move(file, newPath, overwrite: true);
                    _logger.LogDebug("Moved chapter file for item {Id}: {Old} -> {New}", id, file, newPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not move chapter file {File} back next to media", file);
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
                    _logger.LogInformation("Removed empty legacy chapters folder {Path}", LegacyChaptersFolderPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not remove legacy chapters folder {Path}", LegacyChaptersFolderPath);
            }

            var introSkipperPath = Path.Join(ApplicationPaths.DataPath, IntroSkipperDataDir);
            try
            {
                if (Directory.Exists(introSkipperPath) && Directory.GetFileSystemEntries(introSkipperPath).Length == 0)
                {
                    Directory.Delete(introSkipperPath);
                    _logger.LogInformation("Removed empty legacy data folder {Path}", introSkipperPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not remove legacy data folder {Path}", introSkipperPath);
            }
        }
    }
}
