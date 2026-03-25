using System.Collections.Generic;

namespace Jellyfin.Plugin.JellySub.Models;

/// <summary>
/// All available metadata used to search for subtitles across sources.
/// Fields are optional — sources use whatever is available.
/// </summary>
public sealed class SubtitleSearchRequest
{
    /// <summary>Jellyfin item ID (used to fetch missing metadata server-side).</summary>
    public string? ItemId { get; set; }

    /// <summary>Movie or series title.</summary>
    public string? Title { get; set; }

    /// <summary>Series title (for episodes; Title then holds episode title).</summary>
    public string? SeriesTitle { get; set; }

    /// <summary>Release year.</summary>
    public int? Year { get; set; }

    /// <summary>IMDb ID including the "tt" prefix (e.g. "tt0133093").</summary>
    public string? ImdbId { get; set; }

    /// <summary>Season number (episodes only).</summary>
    public int? SeasonNumber { get; set; }

    /// <summary>Episode number (episodes only).</summary>
    public int? EpisodeNumber { get; set; }

    /// <summary>Absolute path to the media file on the server (used for movie-hash matching).</summary>
    public string? MediaFilePath { get; set; }

    /// <summary>
    /// Desired subtitle languages as BCP-47 codes.
    /// Empty list means "return all languages".
    /// </summary>
    public List<string> Languages { get; set; } = new();

    /// <summary>Skip results with fewer downloads than this threshold.</summary>
    public int MinDownloadCount { get; set; } = 0;
}
