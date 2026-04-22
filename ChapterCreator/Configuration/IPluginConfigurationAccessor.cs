namespace ChapterCreator.Configuration;

/// <summary>
/// Provides access to the current plugin configuration.
/// </summary>
public interface IPluginConfigurationAccessor
{
    /// <summary>
    /// Gets the current plugin configuration.
    /// </summary>
    /// <returns>The active plugin configuration.</returns>
    PluginConfiguration GetConfiguration();
}
