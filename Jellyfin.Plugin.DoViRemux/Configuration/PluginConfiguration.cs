using MediaBrowser.Model.Plugins;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Comma-separated list of library IDs that we'll constrain remuxing to
    /// </summary>
    public string? OnlyRemuxLibraries { get; set; }

    public PluginConfiguration()
    {
    }
}