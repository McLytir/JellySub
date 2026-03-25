using System;

namespace Jellyfin.Plugin.JellySub.Models;

/// <summary>
/// A single subtitle candidate returned by a source.
/// </summary>
public sealed class SubtitleResult
{
    /// <summary>Source-internal identifier (opaque string — passed back on download).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Which source produced this result (<see cref="Sources.SourceIds"/>).</summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>Release / subtitle title as shown on the site.</summary>
    public string ReleaseName { get; set; } = string.Empty;

    /// <summary>BCP-47 language code (e.g. "en").</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Human-readable language name.</summary>
    public string LanguageName { get; set; } = string.Empty;

    /// <summary>Total download count reported by the source (0 if unavailable).</summary>
    public int DownloadCount { get; set; }

    /// <summary>Uploader / submitter name (empty if unavailable).</summary>
    public string Uploader { get; set; } = string.Empty;

    /// <summary>Upload date (null if unavailable).</summary>
    public DateTime? UploadDate { get; set; }

    /// <summary>Subtitle format (typically "srt").</summary>
    public string Format { get; set; } = "srt";

    /// <summary>True when the source matched this result via movie-file hash.</summary>
    public bool IsHashMatch { get; set; }

    /// <summary>True when the subtitle is flagged as hearing-impaired / SDH.</summary>
    public bool IsHearingImpaired { get; set; }

    /// <summary>True when the subtitle is a machine / AI translation.</summary>
    public bool IsMachineTranslated { get; set; }

    /// <summary>
    /// Release group or encoding tag extracted from the filename
    /// (e.g. "BluRay.x264-GROUP").  Used for guided series matching.
    /// </summary>
    public string ReleaseGroup { get; set; } = string.Empty;

    /// <summary>
    /// Direct URL the server will hit to download this subtitle.
    /// May point to a zip archive — <see cref="Services.SubtitleFileService"/> handles extraction.
    /// </summary>
    public string DownloadUrl { get; set; } = string.Empty;
}
