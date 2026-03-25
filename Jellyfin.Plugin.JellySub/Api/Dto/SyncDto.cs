using System.Collections.Generic;
using Jellyfin.Plugin.JellySub.Models;

namespace Jellyfin.Plugin.JellySub.Api.Dto;

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

public sealed class SyncResponseDto
{
    public bool   Success    { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public string ToolOutput { get; set; } = string.Empty;
    public string? Error     { get; set; }
}

public sealed class SyncToolsStatusDto
{
    public IReadOnlyList<SyncToolStatus> Tools { get; set; } = new List<SyncToolStatus>();
}

public sealed class InstallToolRequestDto
{
    public string ToolId { get; set; } = string.Empty;
}

public sealed class InstallToolResponseDto
{
    public bool   Success { get; set; }
    public string Output  { get; set; } = string.Empty;
}
