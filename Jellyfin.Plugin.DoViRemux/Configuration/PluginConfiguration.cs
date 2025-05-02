using MediaBrowser.Model.Plugins;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Comma-separated list of library IDs that we'll constrain remuxing to
    /// </summary>
    /// <remarks>
    /// Can be a library ID, show ID, season ID, etc. Anything in the item's hierarchy.
    /// </remarks>
    public string? IncludeAncestorIds { get; set; }

    /// <summary>
    /// Parsed form of IncludeAncestorIds
    /// </summary>
    public Guid[] IncludeAncestors => IncludeAncestorIds?.Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(Guid.Parse)
        .ToArray()
        ?? [];
}