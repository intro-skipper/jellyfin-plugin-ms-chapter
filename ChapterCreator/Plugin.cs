using System;
using System.Collections.Generic;
using System.Globalization;
using ChapterCreator.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace ChapterCreator
{
    /// <summary>
    /// The main plugin.
    /// </summary>
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        private readonly ILibraryManager _libraryManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="Plugin"/> class.
        /// </summary>
        /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
        /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
        /// <param name="libraryManager">Library manager.</param>
        public Plugin(
            IApplicationPaths applicationPaths,
            IXmlSerializer xmlSerializer,
            ILibraryManager libraryManager)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;

            _libraryManager = libraryManager;
        }

        /// <inheritdoc />
        public override string Name => "Chapter Creator";

        /// <inheritdoc />
        public override Guid Id => Guid.Parse("6B0E323A-4AEE-4B10-813F-1E060488AE90");

        /// <summary>
        /// Gets the current plugin instance.
        /// </summary>
        public static Plugin? Instance { get; private set; }

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
    }
}
