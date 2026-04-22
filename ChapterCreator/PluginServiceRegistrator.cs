using ChapterCreator.Configuration;
using ChapterCreator.Managers;
using ChapterCreator.SheduledTasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace ChapterCreator;

/// <summary>
/// Register Chapter Creator services.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<IPluginConfigurationAccessor, PluginConfigurationAccessor>();
        serviceCollection.AddSingleton<IChapterFileManager, ChapterFileManager>();
        serviceCollection.AddSingleton<IChapterOutputService, ChapterOutputService>();
        serviceCollection.AddSingleton<ILegacyChapterMigrator, LegacyChapterMigrator>();
        serviceCollection.AddSingleton<IQueueManager, QueueManager>();
        serviceCollection.AddSingleton<IChapterTaskRunner, BaseChapterTask>();
    }
}
