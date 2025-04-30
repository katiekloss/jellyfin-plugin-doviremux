using Jellyfin.Data.Enums;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DoViRemuxPlugin;

public class RemuxLibraryTask(IItemRepository _itemRepo,
                              IMediaSourceManager _sourceManager,
                              ITranscodeManager _transcodeManager,
                              IPluginManager _pluginManager,
                              ILogger<RemuxLibraryTask> _logger)
    : IScheduledTask
{
    public string Name => "Remux Dolby Vision MKVs";

    public string Key => nameof(RemuxLibraryTask);

    public string Description => "";

    public string Category => "Library";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return [new TaskTriggerInfo() { Type = TaskTriggerInfo.TriggerDaily, TimeOfDayTicks = 0 }];
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var plugin = _pluginManager.GetPlugin(Plugin.OurGuid)?.Instance as Plugin
            ?? throw new Exception("Can't get plugin instance");

        var configuration = plugin.Configuration;

        var allItems = _itemRepo.GetItems(new InternalItemsQuery
        {
            MediaTypes = [MediaType.Video],
            AncestorIds = configuration.OnlyRemuxLibraries?.Split(",").Select(Guid.Parse).ToArray()
                ?? []
        });

        foreach (var item in allItems.Items)
        {
            try
            {
                await ProcessOneItem(item, cancellationToken);
            }
            catch (Exception x)
            {
                _logger.LogWarning(x, "Failed to process {ItemId}", item.Id);
            }
        }
    }

    private async Task ProcessOneItem(BaseItem item, CancellationToken cancellationToken)
    {
        if (item.Container != "mkv") return;

        var streams = _sourceManager.GetMediaStreams(item.Id);
        var doviStream = streams.FirstOrDefault(s => s.Type == MediaBrowser.Model.Entities.MediaStreamType.Video
                                             && s.DvProfile.HasValue);

        if (doviStream is not { DvProfile: 8, DvVersionMajor: 1 }) return;

        // if there's an existing MP4 source, assume we made it.
        // also I can't decide if I like that the model object comes back with services inside it
        // which can run lookups like this. The API is sort of clean, actually, but... am I just a hater?
        var otherSources = item.GetMediaSources(true);
        if (otherSources.Any(s => s.Container == "mp4")) return;

        var ourSource = otherSources.First(s => s.Container == "mkv");

        var inputPath = ourSource.Path;
        var outputPath = $"{inputPath}.mp4";

        // truehd isn't supported by many consumer MP4 decoders even though ffmpeg can do it
        // since it's also dolby, it's used by a lot of DoVi media
        var audioStream = streams.FirstOrDefault(s => s.Type == MediaBrowser.Model.Entities.MediaStreamType.Audio
                                              && s.Codec != "truehd"
                                              && s.Language == "eng")
            // TODO: transcode it instead
            ?? throw new Exception("Couldn't find an appropriate audio stream to copy");

        var remuxRequest = new StreamState(_sourceManager, TranscodingJobType.Progressive, _transcodeManager);

        remuxRequest.MediaSource = ourSource;
        remuxRequest.Request = new StreamingRequestDto
        {
            LiveStreamId = null // i don't remember why this has to be null
        };

        remuxRequest.MediaPath = outputPath;

        // avoids NREs and changes the log filename prefix from "Transcode" to "Remux"
        remuxRequest.OutputContainer = "mp4";
        remuxRequest.OutputAudioCodec = "copy";
        remuxRequest.OutputVideoCodec = "copy";

        string cli = "-analyzeduration 200M -probesize 1G -fflags +genpts ";
        cli += $"-i \"{inputPath}\" ";
        cli += $"-map_metadata -1 -map_chapters -1 -threads 0 -map 0:0 -map 0:{audioStream.Index} -map -0:s ";
        cli += "-codec:v:0 copy -tag:v:0 dvh1 -strict -2 -bsf:v hevc_mp4toannexb -start_at_zero ";
        cli += "-codec:a:0 copy -copyts -avoid_negative_ts disabled -max_muxing_queue_size 2048 ";
        cli += $"\"{outputPath}\"";

        var remuxCancelToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var job = await _transcodeManager.StartFfMpeg(remuxRequest, outputPath, cli, Guid.Empty, TranscodingJobType.Progressive, remuxCancelToken);
    }
}