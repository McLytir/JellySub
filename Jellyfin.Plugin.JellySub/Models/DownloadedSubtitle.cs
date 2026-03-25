namespace Jellyfin.Plugin.JellySub.Models;

/// <summary>
/// Result of a subtitle download-and-save operation.
/// </summary>
public sealed class DownloadedSubtitle
{
    /// <summary>Whether the download and file-write succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Absolute path of the saved .srt file on the server.</summary>
    public string SavedPath { get; set; } = string.Empty;

    /// <summary>Human-readable error message when <see cref="Success"/> is false.</summary>
    public string? Error { get; set; }

    /// <summary>BCP-47 language code of the downloaded subtitle.</summary>
    public string Language { get; set; } = string.Empty;
}
