using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Streaming;

public class RemuxScanTask(IItemRepository _itemRepo, IMediaSourceManager _sourceManager, ITranscodeManager _transcodeManager)
    : ILibraryPostScanTask
{
    public async Task Run(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var allItems = _itemRepo.GetItems(new MediaBrowser.Controller.Entities.InternalItemsQuery
        {
            MediaTypes = [MediaType.Video],
            ItemIds = [Guid.Parse("a38eb7a320aff68b1371f98fa9bab20c")]
        });

        var originalItem = allItems.Items[0];
        var inputPath = originalItem.Path;
        var outputPath = $"{inputPath}.mp4";

        var streams = _sourceManager.GetMediaStreams(allItems.Items[0].Id);
        var doviStreams = streams.Where(s => s.Type == MediaBrowser.Model.Entities.MediaStreamType.Video
                                             && s.DvProfile.HasValue).ToList();
        
        var remuxRequest = new StreamState(_sourceManager, TranscodingJobType.Progressive, _transcodeManager);

        // TODO: does this return multiple versions of an item?
        remuxRequest.MediaSource = allItems.Items[0].GetMediaSources(true)[0];
        remuxRequest.Request = new StreamingRequestDto
        {
            LiveStreamId = null
        };

        remuxRequest.MediaPath = outputPath;

        // avoids NREs and changes the log filename prefix from "Transcode" to "Remux"
        remuxRequest.OutputContainer = "mp4";
        remuxRequest.OutputAudioCodec = "copy";
        remuxRequest.OutputVideoCodec = "copy";

        string cli = "-analyzeduration 200M -probesize 1G -fflags +genpts ";
        cli += $"-i \"{inputPath}\" ";
        cli += "-map_metadata -1 -map_chapters -1 -threads 0 -map 0:0 -map 0:1 -map -0:s ";
        cli += "-codec:v:0 copy -tag:v:0 dvh1 -strict -2 -bsf:v hevc_mp4toannexb -start_at_zero ";
        cli += "-codec:a:0 copy -copyts -avoid_negative_ts disabled -max_muxing_queue_size 2048 ";
        cli += $"\"{outputPath}\"";
 
        var remuxCancelToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await _transcodeManager.StartFfMpeg(remuxRequest, outputPath, cli, Guid.Empty, TranscodingJobType.Progressive, remuxCancelToken);
    }
}