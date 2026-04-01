namespace Jellyfin.Plugin.JellySub.Api.Dto;

/// <summary>Request payload for downloading and saving a subtitle.</summary>
public sealed class DownloadRequestDto
{
    /// <summary>Jellyfin item ID of the media file to subtitle.</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>Source ID (<see cref="Sources.SourceIds"/>).</summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>Source-internal subtitle ID.</summary>
    public string SubtitleId { get; set; } = string.Empty;

    /// <summary>Direct download URL from the search result.</summary>
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>BCP-47 language code.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Release name (used for display and release-group extraction).</summary>
    public string ReleaseName { get; set; } = string.Empty;

    /// <summary>Uploader name (used for guided series matching).</summary>
    public string Uploader { get; set; } = string.Empty;

    /// <summary>Release group tag extracted from the filename.</summary>
    public string ReleaseGroup { get; set; } = string.Empty;
}

/// <summary>Response payload for a subtitle download request.</summary>
public sealed class DownloadResponseDto
{
    /// <summary>True when the subtitle was downloaded and saved successfully.</summary>
    public bool Success { get; set; }

    /// <summary>Filesystem path where the subtitle was saved.</summary>
    public string SavedPath { get; set; } = string.Empty;

    /// <summary>Error message when the download or save operation failed.</summary>
    public string? Error { get; set; }
}
