using System.Diagnostics;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DoViRemux;

public class DownmuxWorkflow(PluginConfiguration _configuration,
                             ILogger<DownmuxWorkflow> _logger,
                             ITranscodeManager _transcodeManager,
                             IApplicationPaths _paths)
{
    public async Task<string> Downmux(MediaSourceInfo mediaSource, CancellationToken token)
    {
        var downmuxCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(token);

        // extract the HEVC stream...
        using var ffmpeg = Process.Start(new ProcessStartInfo("ffmpeg")
        {
            Arguments = string.Join(" ", [
                "-y",
                $"-i \"{mediaSource.Path}\"",
                "-dn",
                "-c:v copy",
                "-bsf:v hevc_mp4toannexb",
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

        var buffer = new byte[4 * 1024 * 1024];
        var bytesRead = 0;

        // extremely complicated way of writing a shell pipe
        do
        {
            bytesRead = await ffmpeg.StandardOutput.BaseStream.ReadAsync(buffer, downmuxCancellationSource.Token);
            await doviTool.StandardInput.BaseStream.WriteAsync(buffer, 0, bytesRead);
            await doviTool.StandardInput.BaseStream.FlushAsync();
        }
        while (!ffmpeg.HasExited && bytesRead > 0);

        doviTool.StandardInput.Close();

        // then remux the HEVC stream into an MP4 with the right DoVi side data
        // indicating it's now profile 8.1
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

        while (!mp4box.HasExited)
        {
            await Task.Delay(1000, downmuxCancellationSource.Token);
        }

        return "/tmp/final.mp4";
    }
}