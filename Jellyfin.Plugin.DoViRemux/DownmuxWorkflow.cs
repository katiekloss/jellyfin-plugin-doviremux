using System.Diagnostics;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DoViRemux;

// A super gross C# implementation of the following reddit comment:
// https://old.reddit.com/r/ffmpeg/comments/11gu4o4/comment/jn5gman
//
// We only need to go as far as the mp4box step, and then the main
// RemuxLibraryTask can handle the rest (it just copies the video
// stream from our MP4 instead of the original MKV)
public class DownmuxWorkflow(IPluginManager _pluginManager,
                             ILogger<DownmuxWorkflow> _logger,
                             IApplicationPaths _paths)
{
    public async Task<string> Downmux(MediaSourceInfo mediaSource, CancellationToken token)
    {
        var configuration = (_pluginManager.GetPlugin(Plugin.OurGuid)?.Instance as Plugin)?.Configuration
            ?? throw new Exception("Can't get plugin configuration");

        string ffmpegOutputPath = Path.Combine(_paths.TempDirectory, $"ffmpeg_{mediaSource.Id}.hevc");
        string doviToolOutputPath = Path.Combine(_paths.TempDirectory, $"dovi_tool_{mediaSource.Id}.hevc");
        string mp4boxOutputPath = Path.Combine(_paths.TempDirectory, $"{mediaSource.Id}_profile8.mp4");

        // extract the HEVC stream...
        using var ffmpeg = new Process()
        {
            // technically this should use the configured path, since it can be overridden with a CLI argument
            StartInfo = new ProcessStartInfo("ffmpeg")
            {
                Arguments = string.Join(" ", [
                    "-y",
                    $"-i \"{mediaSource.Path}\"",
                    "-dn",
                    "-c:v copy",
                    "-f hevc", // trivia: using the hevc muxer automatically adds the hevc_mp4toannexb bitstream filter
                    ffmpegOutputPath
                ]),
                RedirectStandardError = true
            }
        };
        
        _logger.LogInformation("{Command} {Arguments}", ffmpeg.StartInfo.FileName, ffmpeg.StartInfo.Arguments);
        ffmpeg.Start();

        _ = WriteStreamToLog(Path.Combine(_paths.LogDirectoryPath, $"ffmpeg_hevc_{mediaSource.Id}_{Guid.NewGuid().ToString()[..8]}.log"),
                             ffmpeg.StandardError.BaseStream,
                             ffmpeg,
                             token)
            .ConfigureAwait(false);

        await ffmpeg.WaitForExitAsync().ConfigureAwait(false);

        // and feed it into dovi_tool, discarding the enhancement layer
        using var doviTool = new Process()
        {
            StartInfo = new ProcessStartInfo(configuration.PathToDoviTool)
            {
                Arguments = string.Join(" ", [
                    "-m 2", // convert RPU to 8.1
                    "convert", // modify RPU
                    "--discard", // discard EL
                    $"--input {ffmpegOutputPath}",
                    $"--output {doviToolOutputPath}"
                ]),
                RedirectStandardError = true
            }
        };
        
        _logger.LogInformation("{Command} {Arguments}", doviTool.StartInfo.FileName, doviTool.StartInfo.Arguments);
        doviTool.Start();

        _ = WriteStreamToLog(Path.Combine(_paths.LogDirectoryPath, $"dovi_tool_{mediaSource.Id}_{Guid.NewGuid().ToString()[..8]}.log"),
                             doviTool.StandardError.BaseStream,
                             doviTool,
                             token)
            .ConfigureAwait(false);

        await doviTool.WaitForExitAsync();

        try
        {
            if (!File.Exists(doviToolOutputPath))
            {
                throw new Exception("dovi_tool failed");
            }
        }
        finally
        {
            File.Delete(ffmpegOutputPath);
        }

        // then remux the HEVC stream into an MP4 with the right DoVi side data
        // indicating it's now profile 8.1. I don't think ffmpeg can do this.
        using var mp4box = new Process()
        {
            StartInfo = new ProcessStartInfo(configuration.PathToMP4Box)
            {
                // no clue what any of this does, except for the dvp line
                Arguments = string.Join(" ", [
                    "-add",
                    $"{doviToolOutputPath}:dvp=8.1:xps_inband:hdr=none",
                    "-brand mp42isom",
                    "-ab dby1",
                    "-no-iod",
                    mp4boxOutputPath,
                    $"-tmp {_paths.TempDirectory}"
                ]),
                RedirectStandardError = true
            }
        };
        
        _logger.LogInformation("{Command} {Arguments}", mp4box.StartInfo.FileName, mp4box.StartInfo.Arguments);
        mp4box.Start();

        _ = WriteStreamToLog(Path.Combine(_paths.LogDirectoryPath, $"mp4box_{mediaSource.Id}_{Guid.NewGuid().ToString()[..8]}.log"),
                             mp4box.StandardError.BaseStream,
                             mp4box,
                             token)
            .ConfigureAwait(false);

        await mp4box.WaitForExitAsync(token);

        File.Delete(doviToolOutputPath);

        return mp4boxOutputPath;
    }

    private async Task WriteStreamToLog(string logPath, Stream logStream, Process logProcess, CancellationToken token)
    {
            using var writer = new StreamWriter(File.Open(
                logPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.Read));

            using var reader = new StreamReader(logStream);

            while (logStream.CanRead && !logProcess.HasExited)
            {
                var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                if (!writer.BaseStream.CanWrite)
                {
                    break;
                }
                await writer.WriteLineAsync(line).ConfigureAwait(false);
                if (!writer.BaseStream.CanWrite)
                {
                    break;
                }
                await writer.FlushAsync().ConfigureAwait(false);
            }

    }
}