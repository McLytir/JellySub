namespace Jellyfin.Plugin.JellySub.Sync;

/// <summary>Outcome of a subtitle-sync operation.</summary>
public sealed class SyncResult
{
    /// <summary>True when the sync process exited with code 0.</summary>
    public bool Success { get; set; }

    /// <summary>Absolute path of the synchronised output file.</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>Combined stdout + stderr from the tool process.</summary>
    public string ToolOutput { get; set; } = string.Empty;

    /// <summary>Error description when <see cref="Success"/> is false.</summary>
    public string? Error { get; set; }
}
