using System.Text.Json.Serialization;
using MediaBrowser.Model.Plugins;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Comma-separated list of library IDs that we'll constrain remuxing to
    /// </summary>
    /// <remarks>
    /// Can be a library ID, show ID, season ID, etc. Anything in the item's hierarchy.
    /// </remarks>
    public string IncludeAncestorIds { get; set; } = string.Empty;

    /// <summary>
    /// A user to reference when determining if an item or its remux have been watched,
    /// to skip remuxing or delete the remux, respectively
    /// </summary>
    public string PrimaryUser { get; set; } = string.Empty;

    public bool DownmuxProfile7 { get; set; } = true;

    public string PathToDoviTool { get; set; } = "/Users/katie/src/dovi_tool/target/release/dovi_tool";

    public string PathToMP4Box { get; set; } = "/usr/local/bin/mp4box";

    /// <summary>
    /// Parsed form of IncludeAncestorIds
    /// </summary>
    [JsonIgnore]
    public Guid[] IncludeAncestors => IncludeAncestorIds?.Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(Guid.Parse)
        .ToArray()
        ?? [];
}