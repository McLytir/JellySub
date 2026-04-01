using System.Collections.Generic;
using Jellyfin.Plugin.JellySub.Models;

namespace Jellyfin.Plugin.JellySub.Api.Dto;

/// <summary>Request payload for subtitle synchronization.</summary>
public sealed class SyncRequestDto
{
    /// <summary>Tool ID: "ffsubsync" or "alass".</summary>
    public string ToolId { get; set; } = string.Empty;

    /// <summary>Absolute path to the video file (required for ffsubsync).</summary>
    public string VideoPath { get; set; } = string.Empty;

    /// <summary>Absolute path of the subtitle to sync.</summary>
    public string SubtitlePath { get; set; } = string.Empty;

    /// <summary>Reference subtitle path (required for alass).</summary>
    public string? ReferenceSubtitlePath { get; set; }

    /// <summary>Output path (optional — if empty, appends .synced.srt).</summary>
    public string OutputPath { get; set; } = string.Empty;
}

/// <summary>Response payload for a subtitle synchronization request.</summary>
public sealed class SyncResponseDto
{
    /// <summary>True when subtitle synchronization succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Path to the synchronized subtitle file.</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>Raw output emitted by the synchronization tool.</summary>
    public string ToolOutput { get; set; } = string.Empty;

    /// <summary>Error message when synchronization failed.</summary>
    public string? Error { get; set; }
}

/// <summary>Status payload describing available subtitle synchronization tools.</summary>
public sealed class SyncToolsStatusDto
{
    /// <summary>Known synchronization tools and their availability status.</summary>
    public IReadOnlyList<SyncToolStatus> Tools { get; set; } = new List<SyncToolStatus>();
}

/// <summary>Request payload to install a subtitle synchronization tool.</summary>
public sealed class InstallToolRequestDto
{
    /// <summary>Identifier of the tool to install.</summary>
    public string ToolId { get; set; } = string.Empty;
}

/// <summary>Response payload for a synchronization tool installation.</summary>
public sealed class InstallToolResponseDto
{
    /// <summary>True when the tool installation completed successfully.</summary>
    public bool Success { get; set; }

    /// <summary>Installer output captured during the operation.</summary>
    public string Output { get; set; } = string.Empty;
}
