using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellySub.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySub.Sync;

/// <summary>
/// Manages discovery, installation, and invocation of sync tools (ffsubsync, alass).
/// </summary>
public sealed class SyncToolManager
{
    private readonly IEnumerable<ISyncTool> _tools;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<SyncToolManager> _logger;

    // alass GitHub release API — returns the latest release JSON
    private const string AlassReleasesUrl =
        "https://api.github.com/repos/kaegi/alass/releases/latest";

    public SyncToolManager(
        IEnumerable<ISyncTool> tools,
        IHttpClientFactory httpFactory,
        ILogger<SyncToolManager> logger)
    {
        _tools      = tools;
        _httpFactory = httpFactory;
        _logger     = logger;
    }

    /// <summary>Return current status for all registered sync tools.</summary>
    public IReadOnlyList<SyncToolStatus> GetAllStatuses()
        => _tools.Select(t => t.GetStatus()).ToList();

    /// <summary>Synchronise a subtitle file using the named tool.</summary>
    public Task<SyncResult> SyncAsync(
        string toolId,
        SyncRequest request,
        CancellationToken cancellationToken)
    {
        var tool = _tools.FirstOrDefault(t => t.Id.Equals(toolId, StringComparison.OrdinalIgnoreCase));
        if (tool is null)
        {
            return Task.FromResult(new SyncResult
            {
                Success = false,
                Error   = $"Unknown sync tool '{toolId}'"
            });
        }

        return tool.SyncAsync(request, cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Installation helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Install ffsubsync via pip.  Requires Python 3.7+ on the server.
    /// </summary>
    public async Task<(bool success, string output)> InstallFfsubsyncAsync(
        CancellationToken cancellationToken)
    {
        // Find pip / pip3
        var pip = FindExecutable("pip3") ?? FindExecutable("pip");
        if (pip is null)
        {
            return (false,
                "pip not found. Please install Python 3.7+ on the server and ensure pip is in PATH.");
        }

        _logger.LogInformation("[Install] Running: {Pip} install ffsubsync", pip);
        var (code, output) = await RunProcessAsync(
            pip, "install --upgrade ffsubsync", cancellationToken).ConfigureAwait(false);

        return (code == 0, output);
    }

    /// <summary>
    /// Download the latest alass binary from GitHub Releases and place it in
    /// the plugin's sync-tools directory.
    /// </summary>
    public async Task<(bool success, string output)> InstallAlassAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            // 1. Resolve the correct asset for this OS/arch
            var assetUrl = await ResolveAlassAssetUrlAsync(cancellationToken).ConfigureAwait(false);
            if (assetUrl is null)
            {
                return (false, "Could not find an alass release asset for this platform.");
            }

            // 2. Download the binary
            var client = _httpFactory.CreateClient("JellySub");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/octet-stream");
            var bytes = await client.GetByteArrayAsync(assetUrl, cancellationToken).ConfigureAwait(false);

            // 3. Save to plugin's sync-tools folder
            var destDir = GetSyncToolsDir();
            Directory.CreateDirectory(destDir);
            var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "alass.exe"
                : "alass";
            var destPath = Path.Combine(destDir, exeName);
            await File.WriteAllBytesAsync(destPath, bytes, cancellationToken).ConfigureAwait(false);

            // Make executable on Unix
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start("chmod", $"+x \"{destPath}\"")?.WaitForExit();
            }

            _logger.LogInformation("[Install] alass installed to {Path}", destPath);
            return (true, $"alass installed to {destPath}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Install] alass installation failed");
            return (false, ex.Message);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task<string?> ResolveAlassAssetUrlAsync(CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("JellySub");
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", "JellySub-Plugin/1.0");

        var json = await client.GetStringAsync(AlassReleasesUrl, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("assets", out var assets))
        {
            return null;
        }

        var os   = GetOsTag();
        var arch = GetArchTag();

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? string.Empty;
            if (name.Contains(os,   StringComparison.OrdinalIgnoreCase) &&
                name.Contains(arch, StringComparison.OrdinalIgnoreCase))
            {
                return asset.GetProperty("browser_download_url").GetString();
            }
        }

        return null;
    }

    private static string GetOsTag()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))     return "macos";
        return "linux";
    }

    private static string GetArchTag() =>
        RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.Arm   => "arm",
            _                  => "x86_64",
        };

    private static string GetSyncToolsDir()
    {
        var asmDir = Path.GetDirectoryName(
            System.Reflection.Assembly.GetExecutingAssembly().Location)!;
        return Path.Combine(asmDir, "sync-tools");
    }

    private static string? FindExecutable(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            var full = Path.Combine(dir, name);
            if (File.Exists(full)) return full;
        }

        return null;
    }

    private static async Task<(int code, string output)> RunProcessAsync(
        string exe, string args, CancellationToken ct)
    {
        var sb = new System.Text.StringBuilder();
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
}
