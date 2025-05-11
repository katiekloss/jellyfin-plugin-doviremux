using Jellyfin.Data.Entities;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Streaming;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DoViRemux;

public class RemuxLibraryTask(IItemRepository _itemRepo,
                              IMediaSourceManager _sourceManager,
                              ITranscodeManager _transcodeManager,
                              PluginConfiguration _configuration,
                              ILogger<RemuxLibraryTask> _logger,
                              IApplicationPaths _paths,
                              ILibraryManager _libraryManager,
                              IUserDataManager _userDataManager,
                              IUserManager _userManager,
                              DownmuxWorkflow _downmuxWorkflow)
    : IScheduledTask
{
    public string Name => "Remux Dolby Vision MKVs";

    public string Key => nameof(RemuxLibraryTask);

    public string Description => "Remuxes MKVs containing Dolby Vision 8.1 metadata into MP4";

    public string Category => "Dolby Vision Remux Plugin";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var primaryUser = _configuration.PrimaryUser is not null
            ? _userManager.GetUserByName(_configuration.PrimaryUser)
                ?? throw new Exception($"Primary user '{_configuration.PrimaryUser}' does not exist")
            : null;

        var itemsToProcess = _itemRepo.GetItems(new InternalItemsQuery
        {
            MediaTypes = [MediaType.Video],
            AncestorIds = _configuration.IncludeAncestors
        })
            .Items
            .Cast<Video>() // has some additional properties (that I don't remember if we use or not)
            .Where(i => !cancellationToken.IsCancellationRequested && ShouldProcessItem(i, primaryUser))
            .ToList();

        var i = 0.0;
        foreach (var item in itemsToProcess)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await ProcessOneItem(item, cancellationToken);
            }
            catch (Exception x)
            {
                _logger.LogWarning(x, "Failed to process {ItemId}", item.Id);
            }

            progress.Report(++i / itemsToProcess.Count * 100);
        }

        if (itemsToProcess.Count > 0)
        {
            _libraryManager.QueueLibraryScan();
        }
    }

    private bool ShouldProcessItem(Video item, User? primaryUser)
    {
        if (item.Container != "mkv") return false;

        if (primaryUser is not null)
        {
            var userData = _userDataManager.GetUserData(primaryUser, item);
            if (userData is { Played: true }) return false;
        }

        var streams = _sourceManager.GetMediaStreams(item.Id);
        var doviStream = streams.FirstOrDefault(s => s.Type == MediaBrowser.Model.Entities.MediaStreamType.Video
                                             && s.DvProfile.HasValue);

        // There used to be constraints here about which profiles we support remuxing, but LG TVs aren't consistent about
        // which ones THEY support. Mine stutters when playing profile 7 but some people have success with it,
        // so we'll remux everything just in case.
        //
        // Jellyfin itself will likely not be happy with profile 5, since there's no HDR10 fallback, so things
        // like trickplay/thumbnail images (or even the entire video) may show up as the silly purple-and-green versions.
        if (doviStream?.DvProfile is null) return false;

        // if there's an existing MP4 source, assume we made it.
        // also I can't decide if I like that the model object comes back with services inside it
        // which can run lookups like this. The API is sort of clean, actually, but... am I just a hater?
        var otherSources = item.GetMediaSources(true);
        if (otherSources.Any(s => s.Container == "mp4")) return false;

        // or if there's an unmerged, standalone item at the expected path
        if (_libraryManager.FindByPath(item.Path + ".mp4", false) is not null) return false;

        return true;
    }

    private async Task ProcessOneItem(Video item, CancellationToken cancellationToken)
    {
        var streams = _sourceManager.GetMediaStreams(item.Id);
        var otherSources = item.GetMediaSources(true);
        var ourSource = otherSources.First(s => s.Container == "mkv");

        // we remux to a temporary file first, then move it to the final directory.
        // this improves performance when jellyfin's temp directory is on a separate
        // drive from the original media, because there's no simultaneous IO. It also
        // avoids the problem of jellyfin trying to process the file before it's done,
        // which can impact things like trickplay (or anything that uses ffprobe,
        // though the faststart flag can help with that)
        var inputPath = ourSource.Path;
        var finalPath = $"{inputPath}.mp4";
        var outputPath = Path.Combine(_paths.TempDirectory, finalPath.GetHashCode() + ".mp4");

        if (File.Exists(finalPath))
        {
            throw new Exception($"File already exists at {finalPath}");
        }

        if (File.Exists(outputPath))
        {
            _logger.LogWarning("Deleting temporary file at {OutputPath}", outputPath);
            File.Delete(outputPath);
        }

        if (streams.Any(s => s.DvProfile == 7))
        {
            var downmuxedVideo = await _downmuxWorkflow.Downmux(ourSource, cancellationToken);
            // todo: use me instead
            return;
        }

        // truehd isn't supported by many consumer MP4 decoders even though ffmpeg can do it.
        // it's found on a lot of DoVi media (cough particularly hybrid remuxes cough),
        // but media like Bluray is required to have an AAC/AC3/whatever fallback stream for compatibility
        var audioStreams = streams.Where(s => s.Type == MediaBrowser.Model.Entities.MediaStreamType.Audio
                                              && s.Codec != "truehd")
                                  .Select((audioStream, i) => new { audioStream.Index, OutputIndex = i, audioStream.Language})
                                  .ToList();
        if (audioStreams.Count == 0)
        {
            // TODO: transcode it instead
            throw new Exception("Couldn't find an appropriate audio stream to copy");
        }

        // PGS subtitles aren't supported by mp4. Technically we can use the copy codec
        // and most decoders will know how to use subrip subtitles, but mov_text is standard
        var subtitles = streams
            .Where(s => s.Type == MediaBrowser.Model.Entities.MediaStreamType.Subtitle
                        && s.IsTextSubtitleStream)
            .Select((subtitle, i) => new { subtitle.Index, OutputIndex = i, Codec = "mov_text", Lang = subtitle.Language })
            .ToList();

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
        cli += $"-map_metadata -1 -map_chapters -1 -threads 0 -map 0:0 ";
        cli += string.Concat(audioStreams.Select(a => $"-map 0:{a.Index} "));
        cli += string.Concat(subtitles.Select(s => $"-map 0:{s.Index} "));
        cli += "-codec:v:0 copy -tag:v:0 dvh1 -strict experimental -bsf:v hevc_mp4toannexb -start_at_zero ";
        cli += string.Concat(audioStreams.Select(a => $"-codec:a:{a.OutputIndex} copy "));
        cli += string.Concat(subtitles.Select(s => $"-codec:s:{s.OutputIndex} {s.Codec} "));
        cli += string.Concat(audioStreams.Select(a => $"-metadata:s:a:{a.OutputIndex} language=\"{a.Language}\" "));
        cli += string.Concat(subtitles.Select(s => $"-metadata:s:s:{s.OutputIndex} language=\"{s.Lang}\" "));
        cli += "-copyts -avoid_negative_ts disabled -max_muxing_queue_size 2048 ";
        cli += $"\"{outputPath}\"";

        cancellationToken.ThrowIfCancellationRequested();

        var remuxCancelToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var job = await _transcodeManager.StartFfMpeg(remuxRequest, outputPath, cli, Guid.Empty, TranscodingJobType.Progressive, remuxCancelToken);
        
        while (!cancellationToken.IsCancellationRequested && !job.HasExited)
        {
            await Task.Delay(1000, cancellationToken);
        }

        try
        {
            File.Move(outputPath, finalPath);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }
}