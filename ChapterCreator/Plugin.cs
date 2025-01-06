using System;
using System.Collections.Generic;
using System.Globalization;
using ChapterCreator.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Entities;
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
        private readonly IItemRepository _itemRepository;

        /// <summary>
        /// Initializes a new instance of the <see cref="Plugin"/> class.
        /// </summary>
        /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
        /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
        /// <param name="libraryManager">Library manager.</param>
        /// <param name="itemRepository">Item repository.</param>
        public Plugin(
            IApplicationPaths applicationPaths,
            IXmlSerializer xmlSerializer,
            ILibraryManager libraryManager,
            IItemRepository itemRepository)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;

            _libraryManager = libraryManager;
            _itemRepository = itemRepository;
        }

        /// <inheritdoc />
        public override string Name => "Chapter Creator";

        /// <inheritdoc />
        public override Guid Id => Guid.Parse("e22fb8f5-bc98-4f76-9be4-87de302a97ea");

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

        /// <summary>
        /// Gets all chapters for this item.
        /// </summary>
        /// <param name="id">Item id.</param>
        /// <returns>List of chapters.</returns>
        internal IReadOnlyList<ChapterInfo> GetChapters(Guid id)
        {
            var item = GetItem(id);
            if (item == null)
            {
                return [];
            }

            return _itemRepository.GetChapters(item);
        }
    }
}
