using System;
using System.Collections.Generic;
using Jellyfin.Plugin.JellySub.Models;

namespace Jellyfin.Plugin.JellySub.Api.Dto;

/// <summary>Search results returned to the web UI.</summary>
public sealed class SearchResultDto
{
    /// <summary>Title used for the subtitle search.</summary>
    public string? SearchTitle { get; set; }

    /// <summary>Year used for the subtitle search, when available.</summary>
    public int? SearchYear { get; set; }

    /// <summary>Subtitle candidates returned by all enabled sources.</summary>
    public IReadOnlyList<SubtitleResultDto> Results { get; set; } = Array.Empty<SubtitleResultDto>();

    /// <summary>Error message when the search could not be completed.</summary>
    public string? Error { get; set; }
}

/// <summary>JSON-serialisable projection of <see cref="SubtitleResult"/>.</summary>
public sealed class SubtitleResultDto
{
    /// <summary>Source-internal subtitle identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Identifier of the source that produced this result.</summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>Human-readable name of the subtitle source.</summary>
    public string SourceName { get; set; } = string.Empty;

    /// <summary>Release or subtitle title shown by the source.</summary>
    public string ReleaseName { get; set; } = string.Empty;

    /// <summary>BCP-47 language code for the subtitle.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Human-readable subtitle language name.</summary>
    public string LanguageName { get; set; } = string.Empty;

    /// <summary>Total download count reported by the source.</summary>
    public int DownloadCount { get; set; }

    /// <summary>Uploader or submitter name, when available.</summary>
    public string Uploader { get; set; } = string.Empty;

    /// <summary>Upload date reported by the source, when available.</summary>
    public DateTime? UploadDate { get; set; }

    /// <summary>True when the source matched the subtitle by media hash.</summary>
    public bool IsHashMatch { get; set; }

    /// <summary>True when the subtitle is marked as hearing-impaired or SDH.</summary>
    public bool IsHearingImpaired { get; set; }

    /// <summary>True when the subtitle is marked as machine translated.</summary>
    public bool IsMachineTranslated { get; set; }

    /// <summary>Release group or encoding tag extracted from the release name.</summary>
    public string ReleaseGroup { get; set; } = string.Empty;

    /// <summary>Direct URL used by the server to download the subtitle.</summary>
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>Creates a DTO from an internal subtitle result.</summary>
    public static SubtitleResultDto From(SubtitleResult r, string sourceName) => new()
    {
        Id                 = r.Id,
        SourceId           = r.SourceId,
        SourceName         = sourceName,
        ReleaseName        = r.ReleaseName,
        Language           = r.Language,
        LanguageName       = r.LanguageName,
        DownloadCount      = r.DownloadCount,
        Uploader           = r.Uploader,
        UploadDate         = r.UploadDate,
        IsHashMatch        = r.IsHashMatch,
        IsHearingImpaired  = r.IsHearingImpaired,
        IsMachineTranslated = r.IsMachineTranslated,
        ReleaseGroup       = r.ReleaseGroup,
        DownloadUrl        = r.DownloadUrl,
    };
}
