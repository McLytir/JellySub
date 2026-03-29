using System;
using System.Collections.Generic;
using Jellyfin.Plugin.JellySub.Models;

namespace Jellyfin.Plugin.JellySub.Api.Dto;

/// <summary>Search results returned to the web UI.</summary>
public sealed class SearchResultDto
{
    public string? SearchTitle { get; set; }
    public int? SearchYear { get; set; }
    public IReadOnlyList<SubtitleResultDto> Results { get; set; } = Array.Empty<SubtitleResultDto>();
    public string? Error { get; set; }
}

/// <summary>JSON-serialisable projection of <see cref="SubtitleResult"/>.</summary>
public sealed class SubtitleResultDto
{
    public string   Id               { get; set; } = string.Empty;
    public string   SourceId         { get; set; } = string.Empty;
    public string   SourceName       { get; set; } = string.Empty;
    public string   ReleaseName      { get; set; } = string.Empty;
    public string   Language         { get; set; } = string.Empty;
    public string   LanguageName     { get; set; } = string.Empty;
    public int      DownloadCount    { get; set; }
    public string   Uploader         { get; set; } = string.Empty;
    public DateTime? UploadDate      { get; set; }
    public bool     IsHashMatch      { get; set; }
    public bool     IsHearingImpaired{ get; set; }
    public bool     IsMachineTranslated { get; set; }
    public string   ReleaseGroup     { get; set; } = string.Empty;
    public string   DownloadUrl      { get; set; } = string.Empty;

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
