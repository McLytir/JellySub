namespace Jellyfin.Plugin.JellySub.Api.Dto;

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

public sealed class DownloadResponseDto
{
    public bool   Success   { get; set; }
    public string SavedPath { get; set; } = string.Empty;
    public string? Error    { get; set; }
}
