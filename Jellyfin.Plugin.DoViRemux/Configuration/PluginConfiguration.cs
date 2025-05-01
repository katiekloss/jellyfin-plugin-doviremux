using MediaBrowser.Model.Plugins;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Comma-separated list of library IDs that we'll constrain remuxing to
    /// </summary>
    public string? OnlyRemuxLibraries { get; set; }

    /// <summary>
    /// Parsed form of OnlyRemuxLibraries
    /// </summary>
    public Guid[] LibrariesToRemux => OnlyRemuxLibraries?.Split(",").Select(Guid.Parse).ToArray() ?? [];
}