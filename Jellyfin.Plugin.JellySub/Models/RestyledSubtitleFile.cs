namespace Jellyfin.Plugin.JellySub.Models;

/// <summary>
/// Result of restyling one existing subtitle file on disk.
/// </summary>
public sealed class RestyledSubtitleFile
{
    /// <summary>Original subtitle file path that was processed.</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>Output subtitle file path after restyling.</summary>
    public string SavedPath { get; set; } = string.Empty;

    /// <summary>True when the restyle operation succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Human-readable status string.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Error details when the restyle failed.</summary>
    public string? Error { get; set; }
}
