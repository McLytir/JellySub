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
/// Wraps alass (https://github.com/kaegi/alass).
///
/// alass aligns an out-of-sync subtitle against a reference subtitle
/// (e.g. another language you already have in sync).  It is very fast
/// (seconds) because it never touches the video file.
///
/// Usage pattern:
///   alass &lt;reference.srt&gt; &lt;unsynced.srt&gt; &lt;output.srt&gt;
///
/// Installation:
///   Download the pre-built binary from https://github.com/kaegi/alass/releases
///   and place it anywhere on PATH, or configure the path in plugin settings.
///   The plugin settings page can download and install it automatically.
/// </summary>
public sealed class AlassTool : ISyncTool
{
    private readonly ILogger<AlassTool> _logger;

    public AlassTool(ILogger<AlassTool> logger) => _logger = logger;

    public string Id          => "alass";
    public string DisplayName => "alass";
    public string Description =>
        "Reference-subtitle-based sync — aligns the subtitle against another language sub " +
        "you already have in sync. No video processing needed; runs in seconds.";

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
            return Fail("alass is not installed. Install it via the plugin settings.");
        }

        if (string.IsNullOrWhiteSpace(request.ReferenceSubtitlePath))
        {
            return Fail(
                "alass requires a reference subtitle. " +
                "Provide a subtitle in another language that is already in sync.");
        }

        var output = string.IsNullOrEmpty(request.OutputPath)
            ? Path.ChangeExtension(request.SubtitlePath, ".synced.srt")
            : request.OutputPath;

        // alass <reference.srt> <unsynced.srt> <output.srt>
        var args = $"\"{request.ReferenceSubtitlePath}\" \"{request.SubtitlePath}\" \"{output}\"";

        _logger.LogInformation("[alass] Running: {Exe} {Args}", exe, args);

        var (exitCode, log) = await RunProcessAsync(exe, args, cancellationToken).ConfigureAwait(false);

        if (exitCode != 0)
        {
            return Fail($"alass exited with code {exitCode}.\n{log}");
        }

        return new SyncResult { Success = true, OutputPath = output, ToolOutput = log };
    }

    // ─────────────────────────────────────────────────────────────────────────

    private string? ResolveExecutable()
    {
        var configured = Plugin.Instance?.Configuration.AlassPath;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "alass.exe"
            : "alass";

        var inPath = FindInPath(exeName);
        if (inPath is not null) return inPath;

        // Plugin data directory (where SyncToolManager downloads binaries)
        var dataDir = Plugin.Instance?.GetType().Assembly.Location is { } asmPath
            ? Path.Combine(Path.GetDirectoryName(asmPath)!, "sync-tools")
            : null;

        if (dataDir is not null)
        {
            var local = Path.Combine(dataDir, exeName);
            if (File.Exists(local)) return local;
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
