using System;
using System.Collections.Generic;
using Jellyfin.Plugin.JellySub.Tasks;

namespace Jellyfin.Plugin.JellySub.Api.Dto;

public sealed class ScanStatusDto
{
    public bool IsRunning { get; set; }
    public IReadOnlyList<ScanLogEntryDto> Log { get; set; } = Array.Empty<ScanLogEntryDto>();
}

public sealed class ScanLogEntryDto
{
    public string    ItemTitle  { get; set; } = string.Empty;
    public string    Language   { get; set; } = string.Empty;
    public string    Status     { get; set; } = string.Empty;
    public string    SavedPath  { get; set; } = string.Empty;
    public string?   Error      { get; set; }
    public DateTime  Timestamp  { get; set; }

    public static ScanLogEntryDto From(ScanLogEntry e) => new()
    {
        ItemTitle = e.ItemTitle,
        Language  = e.Language,
        Status    = e.Status,
        SavedPath = e.SavedPath,
        Error     = e.Error,
        Timestamp = e.Timestamp,
    };
}
