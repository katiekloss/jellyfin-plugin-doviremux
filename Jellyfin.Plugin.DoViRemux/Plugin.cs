using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.DoViRemux;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static Guid OurGuid = Guid.Parse("2f215b63-1a73-4193-9102-78f84d027014");
    public override string Name => "DoVi Remux";
    public override Guid Id => OurGuid;

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer)
    {
    }

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return [
            new()
            {
                Name = Name,
                EmbeddedResourcePath = "Jellyfin.Plugin.DoViRemux.Configuration.config.html"
            }
        ];
    }
}