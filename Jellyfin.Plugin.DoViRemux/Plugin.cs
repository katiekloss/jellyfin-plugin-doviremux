using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.DoViRemuxPlugin;

public class Plugin : BasePlugin<PluginConfiguration>
{
    public static Guid OurGuid = Guid.Parse("2f215b63-1a73-4193-9102-78f84d027014");
    public override string Name => nameof(Plugin);
    public override Guid Id => OurGuid;

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer)
    {
    }
}