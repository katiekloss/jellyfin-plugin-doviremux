using Jellyfin.Data.Enums;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DoViRemux;

/// <summary>
/// Links items created by RemuxLibraryTask to their original source item,
/// so they appear as a second version of the same media in the UI
/// </summary>
public class MergeVersionsPostScanTask(ILibraryManager _libraryManager,
                                       IItemRepository _itemRepo,
                                       IPluginManager _pluginManager,
                                       ILogger<MergeVersionsPostScanTask> _logger)
    : ILibraryPostScanTask
{
    public async Task Run(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var plugin = _pluginManager.GetPlugin(Plugin.OurGuid)?.Instance as Plugin
            ?? throw new Exception("Can't get plugin instance");

        var allVideos = _itemRepo.GetItems(new()
        {
            MediaTypes = [MediaType.Video]
        })
            .Items
            .Cast<Video>();

        foreach (var item in allVideos)
        {
            if (item.Container != "mkv" || !item.Path.EndsWith("mkv")) continue;
            if (item.LinkedAlternateVersions.Length > 0 || item.PrimaryVersionId is not null) continue;
            if (item.GetDefaultVideoStream().DvProfile != 8) continue;
            if (_libraryManager.FindByPath(item.Path + ".mp4", false) is not Video remuxedItem) continue;
            if (remuxedItem.PrimaryVersionId is not null) continue;

            _logger.LogInformation("Linking primary {ItemId} to its remux {LinkedItemId}",
                item.Id,
                remuxedItem.Id);

            remuxedItem.SetPrimaryVersionId(item.Id.ToString());
            await remuxedItem.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken);
        }
    }
}