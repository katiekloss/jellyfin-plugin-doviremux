using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;

public class DoViRemuxPlugin : BasePlugin<PluginConfiguration>
{
    public override string Name => nameof(DoViRemuxPlugin);
    public override Guid Id { get; } = Guid.Parse("2f215b63-1a73-4193-9102-78f84d027014");

    public DoViRemuxPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer)
    {
    }
}