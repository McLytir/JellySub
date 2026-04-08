namespace Jellyfin.Plugin.JellySub.Models;

/// <summary>
/// Represents one episode in a series batch-download operation — tracks its chosen
/// subtitle candidate and final download result.
/// </summary>
public sealed class EpisodeSubtitleEntry
{
    /// <summary>Jellyfin item ID of the episode.</summary>
    public string ItemId { get; set; } = string.Empty;

    /// <summary>Display label, e.g. "S01E02 – The Tight Deadline".</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Absolute media file path on the server.</summary>
    public string MediaPath { get; set; } = string.Empty;

    /// <summary>Search title for this media item.</summary>
    public string SearchTitle { get; set; } = string.Empty;

    /// <summary>Series title when this entry represents an episode.</summary>
    public string? SeriesTitle { get; set; }

    /// <summary>Season number.</summary>
    public int SeasonNumber { get; set; }

    /// <summary>Episode number.</summary>
    public int EpisodeNumber { get; set; }

    /// <summary>IMDb ID if available.</summary>
    public string? ImdbId { get; set; }

    /// <summary>True if a subtitle for the requested language already exists.</summary>
    public bool AlreadyHasSubtitle { get; set; }

    /// <summary>
    /// The subtitle candidate chosen for this episode (null = not yet selected or no match found).
    /// </summary>
    public SubtitleResult? ChosenSubtitle { get; set; }

    /// <summary>Result after the download has run (null = not yet downloaded).</summary>
    public DownloadedSubtitle? DownloadResult { get; set; }

    /// <summary>
    /// How this episode's subtitle was matched in guided mode.
    /// "Manual" | "UploaderMatch" | "PatternMatch" | "BestAvailable" | "NotFound"
    /// </summary>
    public string MatchMethod { get; set; } = "NotFound";
}
