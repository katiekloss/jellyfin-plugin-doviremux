using System.Diagnostics;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DoViRemux;

// A super gross C# implementation of the following reddit comment:
// https://old.reddit.com/r/ffmpeg/comments/11gu4o4/comment/jn5gman
//
// We only need to go as far as the mp4box step, and then the main
// RemuxLibraryTask can handle the rest (it just copies the video
// stream from our MP4 instead of the original MKV)
public class DownmuxWorkflow(PluginConfiguration _configuration,
                             ILogger<DownmuxWorkflow> _logger,
                             ITranscodeManager _transcodeManager,
                             IApplicationPaths _paths)
{
    public async Task<string> Downmux(MediaSourceInfo mediaSource, CancellationToken token)
    {
        // extract the HEVC stream...
        using var ffmpeg = Process.Start(new ProcessStartInfo("ffmpeg")
        {
            // trivia: using the hevc muxer automatically adds the hevc_mp4toannexb bitstream filter
            Arguments = string.Join(" ", [
                "-y",
                $"-i \"{mediaSource.Path}\"",
                "-dn",
                "-c:v copy",
                "-f hevc",
                "-"
            ]),
            RedirectStandardOutput = true
        });
        
        if (ffmpeg is null || ffmpeg.HasExited) throw new InvalidOperationException("FFmpeg failed to start");

        // and feed it into dovi_tool, discarding the enhancement layer
        using var doviTool = Process.Start(new ProcessStartInfo(_configuration.PathToDoviTool)
        {
            Arguments = string.Join(" ", [
                "-m 2", // convert RPU to 8.1
                "convert", // modify RPU
                "--discard", // discard EL
                "-",
                "-o /tmp/dovi_tool.hevc"
            ]),
            RedirectStandardInput = true
        });
        
        if (doviTool is null || doviTool.HasExited) throw new InvalidOperationException("dovi_tool failed to start");

        // I'm assuming a bigger buffer is better, when working with 60+ GB files,
        // but maybe not? idk how to determine the ideal buffer size.
        var buffer = new byte[4 * 1024 * 1024];
        var bytesRead = 0;

        // extremely complicated way of writing a shell pipe
        do
        {
            bytesRead = await ffmpeg.StandardOutput.BaseStream.ReadAsync(buffer, token);
            await doviTool.StandardInput.BaseStream.WriteAsync(buffer, 0, bytesRead);
            await doviTool.StandardInput.BaseStream.FlushAsync();
        }
        while (!token.IsCancellationRequested && !ffmpeg.HasExited && bytesRead > 0);

        doviTool.StandardInput.Close();

        // then remux the HEVC stream into an MP4 with the right DoVi side data
        // indicating it's now profile 8.1. I don't think ffmpeg can do this.
        using var mp4box = Process.Start(new ProcessStartInfo(_configuration.PathToMP4Box)
        {
            // no clue what any of this does, except for the dvp line
            Arguments = string.Join(" ", [
                "-add",
                "/tmp/dovi_tool.hevc:dvp=8.1:xps_inband:hdr=none",
                "-brand mp42isom",
                "-ab dby1",
                "-no-iod",
                "/tmp/final.mp4",
                "-tmp /tmp"
            ])
        });

        if (mp4box is null || mp4box.HasExited) throw new InvalidOperationException("mp4box failed to start");

        while (!token.IsCancellationRequested && !mp4box.HasExited)
        {
            await Task.Delay(1000, token);
        }

        File.Delete("/tmp/dovi_tool.hevc");
        return "/tmp/final.mp4";
    }
}