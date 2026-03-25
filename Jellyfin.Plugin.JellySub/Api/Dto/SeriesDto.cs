using System;
using System.Collections.Generic;
using Jellyfin.Plugin.JellySub.Models;

namespace Jellyfin.Plugin.JellySub.Api.Dto;

/// <summary>Request to analyse a series folder's subtitle coverage.</summary>
public sealed class SeriesAnalyzeRequestDto
{
    /// <summary>Jellyfin item ID of the Series (or Season) item.</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>Language to check coverage for.</summary>
    public string Language { get; set; } = string.Empty;
}

/// <summary>One episode row in the series analysis response.</summary>
public sealed class EpisodeEntryDto
{
    public string ItemId          { get; set; } = string.Empty;
    public string Label           { get; set; } = string.Empty;
    public int    SeasonNumber    { get; set; }
    public int    EpisodeNumber   { get; set; }
    public bool   HasSubtitle     { get; set; }
    public string MatchMethod     { get; set; } = string.Empty;
    public SubtitleResultDto? ChosenSubtitle { get; set; }
}

/// <summary>Full analysis result for a series.</summary>
public sealed class SeriesAnalysisDto
{
    public string SeriesTitle { get; set; } = string.Empty;
    public IReadOnlyList<EpisodeEntryDto> Episodes { get; set; } = Array.Empty<EpisodeEntryDto>();
}

/// <summary>Request to batch-download subtitles for a series.</summary>
public sealed class SeriesBatchDownloadRequestDto
{
    public IReadOnlyList<EpisodeBatchItem> Items { get; set; } = Array.Empty<EpisodeBatchItem>();
    public bool RunAutoSync { get; set; } = false;
    public string SyncTool  { get; set; } = string.Empty;
}

public sealed class EpisodeBatchItem
{
    public string ItemId      { get; set; } = string.Empty;
    public string? Label      { get; set; }
    public string MediaPath   { get; set; } = string.Empty;
    public string SourceId    { get; set; } = string.Empty;
    public string SubtitleId  { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string Language    { get; set; } = string.Empty;
    public string Uploader    { get; set; } = string.Empty;
    public string ReleaseGroup{ get; set; } = string.Empty;
    public string ReleaseName { get; set; } = string.Empty;
}

/// <summary>Request to match episode subtitles using the anchor from episode 1.</summary>
public sealed class SeriesMatchRequestDto
{
    public string SeriesItemId   { get; set; } = string.Empty;
    public string Language       { get; set; } = string.Empty;
    public SubtitleResultDto Anchor { get; set; } = new();
}

public sealed class BatchDownloadResultDto
{
    public IReadOnlyList<BatchItemResultDto> Results { get; set; } = Array.Empty<BatchItemResultDto>();
}

public sealed class BatchItemResultDto
{
    public string ItemId    { get; set; } = string.Empty;
    public string Label     { get; set; } = string.Empty;
    public bool   Success   { get; set; }
    public string SavedPath { get; set; } = string.Empty;
    public string? Error    { get; set; }
}
