using System;
using System.Collections.Generic;
using Jellyfin.Plugin.JellySub.Models;

namespace Jellyfin.Plugin.JellySub.Api.Dto;

/// <summary>Request to restyle subtitles under a specific Jellyfin item tree.</summary>
public sealed class RestyleItemSubtitlesRequestDto
{
    /// <summary>Jellyfin item ID whose descendant media subtitles should be restyled.</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>Delete original .srt files after writing the styled .ass version.</summary>
    public bool ReplaceOriginalSrt { get; set; }
}

/// <summary>Request to restyle subtitles across the whole library.</summary>
public sealed class RestyleLibrarySubtitlesRequestDto
{
    /// <summary>Delete original .srt files after writing the styled .ass version.</summary>
    public bool ReplaceOriginalSrt { get; set; }
}

/// <summary>Result of restyling subtitle files in a scope.</summary>
public sealed class RestyleSubtitlesResponseDto
{
    /// <summary>Display name of the processed scope.</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>Number of media items scanned for subtitle files.</summary>
    public int MediaItemsScanned { get; set; }

    /// <summary>Number of subtitle files successfully restyled.</summary>
    public int RestyledCount { get; set; }

    /// <summary>Number of subtitle files skipped because none were found.</summary>
    public int SkippedCount { get; set; }

    /// <summary>Number of subtitle files that failed to restyle.</summary>
    public int FailedCount { get; set; }

    /// <summary>Detailed per-file results.</summary>
    public IReadOnlyList<RestyleSubtitleResultDto> Results { get; set; } = Array.Empty<RestyleSubtitleResultDto>();
}

/// <summary>Detailed result for one subtitle file restyle operation.</summary>
public sealed class RestyleSubtitleResultDto
{
    /// <summary>Media item label or title associated with the subtitle file.</summary>
    public string ItemLabel { get; set; } = string.Empty;

    /// <summary>Original subtitle file path.</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>Styled subtitle output path.</summary>
    public string SavedPath { get; set; } = string.Empty;

    /// <summary>Status text such as Restyled, Skipped, or Failed.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Error details when restyling failed.</summary>
    public string? Error { get; set; }

    /// <summary>Create a DTO from the internal model.</summary>
    public static RestyleSubtitleResultDto From(string itemLabel, RestyledSubtitleFile file) => new()
    {
        ItemLabel = itemLabel,
        SourcePath = file.SourcePath,
        SavedPath = file.SavedPath,
        Status = file.Status,
        Error = file.Error,
    };
}
