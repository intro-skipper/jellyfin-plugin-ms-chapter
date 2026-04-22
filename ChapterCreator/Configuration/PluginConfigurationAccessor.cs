namespace ChapterCreator.Configuration;

/// <summary>
/// Resolves the current plugin configuration from the active plugin instance.
/// </summary>
public sealed class PluginConfigurationAccessor : IPluginConfigurationAccessor
{
    /// <inheritdoc />
    public PluginConfiguration GetConfiguration()
    {
        return Plugin.Instance?.Configuration ?? new PluginConfiguration();
    }
}
