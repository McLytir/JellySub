namespace Jellyfin.Plugin.JellySub.Sync;

/// <summary>Parameters for a subtitle-sync operation.</summary>
public sealed class SyncRequest
{
    /// <summary>Absolute path to the video file (used by ffsubsync for audio analysis).</summary>
    public string VideoPath { get; set; } = string.Empty;

    /// <summary>Absolute path of the subtitle to synchronise.</summary>
    public string SubtitlePath { get; set; } = string.Empty;

    /// <summary>
    /// Optional reference subtitle path (used by alass instead of the video file).
    /// When provided, alass aligns <see cref="SubtitlePath"/> to this reference.
    /// </summary>
    public string? ReferenceSubtitlePath { get; set; }

    /// <summary>
    /// Where to write the synchronised subtitle.
    /// If empty the tool overwrites <see cref="SubtitlePath"/> (or appends ".synced.srt").
    /// </summary>
    public string OutputPath { get; set; } = string.Empty;
}
