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
    /// <summary>Jellyfin item ID for the episode.</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>Display label for the episode.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Season number for the episode.</summary>
    public int SeasonNumber { get; set; }

    /// <summary>Episode number within the season.</summary>
    public int EpisodeNumber { get; set; }

    /// <summary>True when the episode already has a subtitle for the requested language.</summary>
    public bool HasSubtitle { get; set; }

    /// <summary>How the episode subtitle match was determined.</summary>
    public string MatchMethod { get; set; } = string.Empty;

    /// <summary>Subtitle chosen for the episode, when one has been selected.</summary>
    public SubtitleResultDto? ChosenSubtitle { get; set; }
}

/// <summary>Full analysis result for a series.</summary>
public sealed class SeriesAnalysisDto
{
    /// <summary>Display title of the analyzed series.</summary>
    public string SeriesTitle { get; set; } = string.Empty;

    /// <summary>Episode-level analysis entries for the series.</summary>
    public IReadOnlyList<EpisodeEntryDto> Episodes { get; set; } = Array.Empty<EpisodeEntryDto>();
}

/// <summary>Request to batch-download subtitles for a series.</summary>
public sealed class SeriesBatchDownloadRequestDto
{
    /// <summary>Episode subtitle selections to download.</summary>
    public IReadOnlyList<EpisodeBatchItem> Items { get; set; } = Array.Empty<EpisodeBatchItem>();

    /// <summary>True to run subtitle sync automatically after download.</summary>
    public bool RunAutoSync { get; set; } = false;

    /// <summary>Synchronization tool to run when auto-sync is enabled.</summary>
    public string SyncTool { get; set; } = string.Empty;
}

/// <summary>One episode item in a batch subtitle download request.</summary>
public sealed class EpisodeBatchItem
{
    /// <summary>Jellyfin item ID for the episode.</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>Optional display label for the episode.</summary>
    public string? Label { get; set; }

    /// <summary>Absolute filesystem path to the episode media file.</summary>
    public string MediaPath { get; set; } = string.Empty;

    /// <summary>Identifier of the subtitle source.</summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>Source-internal subtitle identifier.</summary>
    public string SubtitleId { get; set; } = string.Empty;

    /// <summary>Direct URL used to download the subtitle.</summary>
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>Language code for the subtitle to download.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Uploader name, when available.</summary>
    public string Uploader { get; set; } = string.Empty;

    /// <summary>Release group extracted from the subtitle release name.</summary>
    public string ReleaseGroup { get; set; } = string.Empty;

    /// <summary>Release name shown by the subtitle source.</summary>
    public string ReleaseName { get; set; } = string.Empty;
}

/// <summary>Request to match episode subtitles using the anchor from episode 1.</summary>
public sealed class SeriesMatchRequestDto
{
    /// <summary>Jellyfin item ID of the series being matched.</summary>
    public string SeriesItemId { get; set; } = string.Empty;

    /// <summary>Jellyfin item ID of the anchor episode used for matching.</summary>
    public string AnchorItemId { get; set; } = string.Empty;

    /// <summary>Language code to match across the series.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Anchor subtitle result used to infer matches for other episodes.</summary>
    public SubtitleResultDto Anchor { get; set; } = new();
}

/// <summary>Result payload for a batch subtitle download operation.</summary>
public sealed class BatchDownloadResultDto
{
    /// <summary>Per-item results for the batch operation.</summary>
    public IReadOnlyList<BatchItemResultDto> Results { get; set; } = Array.Empty<BatchItemResultDto>();
}

/// <summary>Result for one item in a batch subtitle download.</summary>
public sealed class BatchItemResultDto
{
    /// <summary>Jellyfin item ID for the processed episode.</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>Display label for the processed episode.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>True when the subtitle download succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Filesystem path where the subtitle was saved.</summary>
    public string SavedPath { get; set; } = string.Empty;

    /// <summary>Error message when the batch item failed.</summary>
    public string? Error { get; set; }
}
