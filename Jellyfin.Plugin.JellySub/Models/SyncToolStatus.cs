namespace Jellyfin.Plugin.JellySub.Models;

/// <summary>
/// Installation and version state of a sync tool (ffsubsync or alass).
/// </summary>
public sealed class SyncToolStatus
{
    /// <summary>Tool identifier — "ffsubsync" or "alass".</summary>
    public string ToolId { get; set; } = string.Empty;

    /// <summary>Human-readable tool name.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>One-line description shown in the settings UI.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>True when the tool binary/executable is found and executable.</summary>
    public bool IsInstalled { get; set; }

    /// <summary>Detected version string (empty if not installed or detection failed).</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Resolved path of the executable.</summary>
    public string ExecutablePath { get; set; } = string.Empty;
}
