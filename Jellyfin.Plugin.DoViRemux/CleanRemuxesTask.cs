using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DoViRemux;

public class CleanRemuxesTask(IPluginManager _pluginManager,
                              IUserManager _userManager,
                              IUserDataManager _userDataManager,
                              ILogger<CleanRemuxesTask> _logger,
                              ILibraryManager _libraryManager,
                              IItemRepository _itemRepo)
    : IScheduledTask
{
    public string Name => "Clean up Dolby Vision remuxes";

    public string Key => nameof(CleanRemuxesTask);

    public string Description => "Deletes remuxed items that the primary user has already watched";

    public string Category => "Dolby Vision Remux Plugin";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken = default)
    {
        var plugin = _pluginManager.GetPlugin(Plugin.OurGuid)?.Instance as Plugin
            ?? throw new Exception("Can't get plugin instance");

        var configuration = plugin.Configuration;
        if (string.IsNullOrEmpty(configuration.PrimaryUser))
        {
            return;
        }

        if (_userManager.GetUserByName(configuration.PrimaryUser) is not User primaryUser)
        {
            throw new Exception($"Primary user '{configuration.PrimaryUser}' does not exist");
        }

        var itemsToProcess = _itemRepo.GetItems(new InternalItemsQuery
        {
            MediaTypes = [MediaType.Video]
        })
            .Items
            .Cast<Video>();

        foreach (var item in itemsToProcess)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.Container is null // file is missing or corrupt
                || !item.Container.Contains("mp4") // mp4s return a long list of possible file extensions, only one of which is "mp4"
                || !item.Path.EndsWith(".mkv.mp4")) // what our plugin generates
            {
                continue;
            }

            var primaryItem = _libraryManager.FindByPath(item.Path[..^4], false);
            if (primaryItem is null)
            {
                // assume it's a .mkv.mp4 that we didn't create
                continue;
            }

            var streams = item.GetMediaStreams();
            if (!streams.Any(s => s.DvProfile.HasValue))
            {
                // also make sure it actually contains a DOVI profile
                continue;
            }

            if (!_userDataManager.GetUserData(primaryUser, item).Played
                && !_userDataManager.GetUserData(primaryUser, primaryItem).Played)
            {
                // Jellyfin doesn't consistently mark both items as Played if
                // they've been merged, so we can't rely on the IsPlayed and User parameters
                // in the query to determine if something was REALLY played
                continue;
            }

            _logger.LogInformation("Deleting item {Id} ({Name})", item.Id, item.Name);

            _libraryManager.DeleteItem(item, new() { DeleteFileLocation = true });
        }
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
}