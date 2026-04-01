using System;
using System.Collections.Generic;
using Jellyfin.Plugin.JellySub.Tasks;

namespace Jellyfin.Plugin.JellySub.Api.Dto;

/// <summary>Current status of a background subtitle scan.</summary>
public sealed class ScanStatusDto
{
    /// <summary>True when a scan job is currently running.</summary>
    public bool IsRunning { get; set; }

    /// <summary>Log entries emitted by the scan job.</summary>
    public IReadOnlyList<ScanLogEntryDto> Log { get; set; } = Array.Empty<ScanLogEntryDto>();
}

/// <summary>A single log entry produced during a subtitle scan.</summary>
public sealed class ScanLogEntryDto
{
    /// <summary>Display title of the media item being processed.</summary>
    public string ItemTitle { get; set; } = string.Empty;

    /// <summary>Filesystem path of the media item.</summary>
    public string MediaPath { get; set; } = string.Empty;

    /// <summary>Language requested for this scan attempt.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Status text describing the scan outcome.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Path where the subtitle was saved, when applicable.</summary>
    public string SavedPath { get; set; } = string.Empty;

    /// <summary>Error details when the scan attempt failed.</summary>
    public string? Error { get; set; }

    /// <summary>Timestamp recorded for the log entry.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Creates a DTO from an internal scan log entry.</summary>
    public static ScanLogEntryDto From(ScanLogEntry e) => new()
    {
        ItemTitle = e.ItemTitle,
        MediaPath = e.MediaPath,
        Language  = e.Language,
        Status    = e.Status,
        SavedPath = e.SavedPath,
        Error     = e.Error,
        Timestamp = e.Timestamp,
    };
}
