using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellySub.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySub.Sync;

/// <summary>
/// Wraps ffsubsync (https://github.com/smacke/ffsubsync).
///
/// ffsubsync uses audio-waveform analysis to re-align a subtitle to a video file.
/// It works best when you have no reference subtitle — it listens to the audio.
/// Typical runtime: 1–5 minutes per episode depending on hardware.
///
/// Installation (server-side):
///   pip install ffsubsync          (requires Python 3.7+)
///   — or install via the plugin settings page (calls pip automatically).
/// </summary>
public sealed class FfsubsyncTool : ISyncTool
{
    private readonly ILogger<FfsubsyncTool> _logger;

    public FfsubsyncTool(ILogger<FfsubsyncTool> logger) => _logger = logger;

    public string Id          => "ffsubsync";
    public string DisplayName => "ffsubsync";
    public string Description =>
        "Audio-based sync — analyses the video's audio track to align the subtitle. " +
        "No reference subtitle needed. Slower but highly accurate. Requires Python 3.7+.";

    // ─────────────────────────────────────────────────────────────────────────

    public SyncToolStatus GetStatus()
    {
        var exe = ResolveExecutable();
        if (exe is null)
        {
            return new SyncToolStatus
            {
                ToolId      = Id,
                DisplayName = DisplayName,
                Description = Description,
                IsInstalled = false,
            };
        }

        var version = RunForOutput(exe, "--version");
        return new SyncToolStatus
        {
            ToolId         = Id,
            DisplayName    = DisplayName,
            Description    = Description,
            IsInstalled    = true,
            Version        = version.Trim(),
            ExecutablePath = exe,
        };
    }

    public async Task<SyncResult> SyncAsync(SyncRequest request, CancellationToken cancellationToken)
    {
        var exe = ResolveExecutable();
        if (exe is null)
        {
            return Fail("ffsubsync is not installed. Install it via the plugin settings.");
        }

        var output = string.IsNullOrEmpty(request.OutputPath)
            ? Path.ChangeExtension(request.SubtitlePath, ".synced.srt")
            : request.OutputPath;

        // ffsubsync <video> -i <subtitle> -o <output>
        var args = $"\"{request.VideoPath}\" -i \"{request.SubtitlePath}\" -o \"{output}\"";

        _logger.LogInformation("[ffsubsync] Running: {Exe} {Args}", exe, args);

        var (exitCode, log) = await RunProcessAsync(exe, args, cancellationToken).ConfigureAwait(false);

        if (exitCode != 0)
        {
            return Fail($"ffsubsync exited with code {exitCode}.\n{log}");
        }

        return new SyncResult { Success = true, OutputPath = output, ToolOutput = log };
    }

    // ─────────────────────────────────────────────────────────────────────────

    private string? ResolveExecutable()
    {
        // 1. User-configured path
        var configured = Plugin.Instance?.Configuration.FfsubsyncPath;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        // 2. Search PATH
        var names = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { "ffsubsync.exe", "ffsubsync" }
            : new[] { "ffsubsync" };

        foreach (var name in names)
        {
            var found = FindInPath(name);
            if (found is not null) return found;
        }

        // 3. Common pip install locations
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            Path.Combine(home, ".local", "bin", "ffsubsync"),
            Path.Combine(home, "AppData", "Local", "Programs", "Python", "Scripts", "ffsubsync.exe"),
            "/usr/local/bin/ffsubsync",
            "/usr/bin/ffsubsync",
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        return null;
    }

    private static string? FindInPath(string name)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            var full = Path.Combine(dir, name);
            if (File.Exists(full)) return full;
        }

        return null;
    }

    private static string RunForOutput(string exe, string args)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName               = exe,
                Arguments              = args,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
            });
            proc?.WaitForExit(5000);
            return proc?.StandardOutput.ReadToEnd() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static async Task<(int exitCode, string output)> RunProcessAsync(
        string exe,
        string args,
        CancellationToken ct)
    {
        var sb = new StringBuilder();
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName               = exe,
                Arguments              = args,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
            }
        };

        proc.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
        proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        return (proc.ExitCode, sb.ToString());
    }

    private static SyncResult Fail(string error) =>
        new() { Success = false, Error = error };
}
