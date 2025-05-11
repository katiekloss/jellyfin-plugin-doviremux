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
        var ffmpeg = Process.Start(new ProcessStartInfo("ffmpeg")
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

        var doviTool = Process.Start(new ProcessStartInfo(_configuration.PathToDoviTool)
        {
            Arguments = string.Join(" ", [
                "-m 2",
                "convert",
                "--discard",
                "-",
                "-o /tmp/dovi_tool.hevc"
            ]),
            RedirectStandardInput = true
        });
        
        if (doviTool is null || doviTool.HasExited) throw new InvalidOperationException("dovi_tool failed to start");

        doviTool.StandardInput.AutoFlush = true;

        var buffer = new byte[4 * 1024 * 1024];
        var bytesRead = 0;

        do
        {
            bytesRead = await ffmpeg.StandardOutput.BaseStream.ReadAsync(buffer, downmuxCancellationSource.Token);
            await doviTool.StandardInput.BaseStream.WriteAsync(buffer, 0, bytesRead);
            await doviTool.StandardInput.BaseStream.FlushAsync();
        }
        while (!ffmpeg.HasExited && bytesRead > 0);

        await doviTool.StandardInput.FlushAsync();
        doviTool.StandardInput.Close();

        return "/tmp/final.mp4";
    }
}