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
        private const string IntroskipperDataDir = "introskipper";
        private const string ChaptersSuffix = "_chapters";

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
        /// Gets the path to the centralized chapters folder inside the introskipper data directory.
        /// </summary>
        public string ChaptersFolderPath => Path.Join(ApplicationPaths.DataPath, IntroskipperDataDir, "chapters");

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
            var previousUseChaptersFolder = Configuration.UseChaptersFolder;

            base.UpdateConfiguration(configuration);

            if (configuration is PluginConfiguration pluginConfig)
            {
                var newValue = pluginConfig.UseChaptersFolder;
                if (newValue != previousUseChaptersFolder)
                {
                    if (newValue)
                    {
                        MigrateToChaptersFolder();
                    }
                    else
                    {
                        MigrateFromChaptersFolder();
                    }
                }
            }
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

        private void MigrateToChaptersFolder()
        {
            _logger.LogInformation("UseChaptersFolder enabled: attempting to move existing chapter files to {Path}", ChaptersFolderPath);

            var items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = [BaseItemKind.Episode, BaseItemKind.Movie],
                Recursive = true,
                IsVirtualItem = false,
            });

            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item.Path))
                {
                    continue;
                }

                // Resolve any VFS symlink to locate the chapter file next to the real media.
                string realPath;
                try
                {
                    realPath = File.ResolveLinkTarget(item.Path, returnFinalTarget: true)?.FullName ?? item.Path;
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Could not resolve symlink for item {Id} at {Path}, skipping", item.Id, item.Path);
                    continue;
                }

                var dir = Path.GetDirectoryName(realPath);
                if (string.IsNullOrEmpty(dir))
                {
                    _logger.LogWarning("Could not determine directory for item {Id} at {Path}, skipping", item.Id, realPath);
                    continue;
                }

                var oldPath = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(realPath)}{ChaptersSuffix}.xml");

                if (!File.Exists(oldPath))
                {
                    continue;
                }

                var newPath = Path.Combine(ChaptersFolderPath, $"{item.Id}{ChaptersSuffix}.xml");

                try
                {
                    Directory.CreateDirectory(ChaptersFolderPath);
                    if (File.Exists(newPath))
                    {
                        _logger.LogDebug("Overwriting existing chapter file at destination: {New}", newPath);
                    }

                    File.Move(oldPath, newPath, overwrite: true);
                    _logger.LogDebug("Moved chapter file for item {Id}: {Old} -> {New}", item.Id, oldPath, newPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not move chapter file for item {Id} from {Old}", item.Id, oldPath);
                }
            }
        }

        private void MigrateFromChaptersFolder()
        {
            _logger.LogInformation("UseChaptersFolder disabled: attempting to move chapter files back next to media from {Path}", ChaptersFolderPath);

            if (!Directory.Exists(ChaptersFolderPath))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(ChaptersFolderPath, $"*{ChaptersSuffix}.xml"))
            {
                var stem = Path.GetFileNameWithoutExtension(file); // "{guid}_chapters"
                var idStr = stem[..^ChaptersSuffix.Length];

                if (!Guid.TryParse(idStr, out var id))
                {
                    _logger.LogDebug("Skipping unrecognised chapter file in chapters folder: {File}", file);
                    continue;
                }

                var item = GetItem(id);
                if (item is null || string.IsNullOrEmpty(item.Path))
                {
                    _logger.LogDebug("No library item found for chapter file {File}, leaving in place", file);
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

                var newPath = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(realPath)}{ChaptersSuffix}.xml");

                try
                {
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
        }
    }
}
