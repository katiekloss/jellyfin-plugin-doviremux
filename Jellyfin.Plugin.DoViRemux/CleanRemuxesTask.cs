using Jellyfin.Data.Entities;
using Jellyfin.Data.Enums;
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

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ExecuteAsyncInternal();
        return Task.CompletedTask;

        void ExecuteAsyncInternal()
        {
            var plugin = _pluginManager.GetPlugin(Plugin.OurGuid)?.Instance as Plugin
                ?? throw new Exception("Can't get plugin instance");

            var configuration = plugin.Configuration;
            if (configuration.PrimaryUser is null)
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

                if (!item.Container.Contains("mp4") // mp4s return a long list of possible file extensions, only one of which is "mp4"
                    || !item.Path.EndsWith(".mkv.mp4"))
                {
                    continue;
                }

                var primaryItem = _libraryManager.FindByPath(item.Path.Substring(0, item.Path.Length - 4), false);
                if (primaryItem is null)
                {
                    // assume it's a .mkv.mp4 that we didn't create
                    continue;
                }

                var mediaSources = item.GetMediaSources(true);
                if (!mediaSources.Any(s => s.VideoStream.DvProfile.HasValue))
                {
                    // also make sure it actually contains a DOVI profile
                    continue;
                }

                // Manually marking a merged item as Played will only mark one of the items as Played
                if (_userDataManager.GetUserData(primaryUser, item) is { Played: true }
                    || _userDataManager.GetUserData(primaryUser, primaryItem) is { Played: true })
                {
                    continue;
                }

                _logger.LogInformation("Deleting item {Id} (\"{Name}\")", item.Id, item.Name);

                _libraryManager.DeleteItem(item, new() { DeleteFileLocation = true });
            }
        }
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
}