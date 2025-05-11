using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.DoViRemux;

public class Registrations : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddTransient(sp => (sp.GetRequiredService<IPluginManager>().GetPlugin(Plugin.OurGuid)?.Instance as Plugin)?.Configuration
            ?? throw new InvalidOperationException("Plugin configuration not registered"));
            
        serviceCollection.AddTransient<RemuxLibraryTask>();
        serviceCollection.AddTransient<CleanRemuxesTask>();
        serviceCollection.AddTransient<DownmuxWorkflow>();
    }
}