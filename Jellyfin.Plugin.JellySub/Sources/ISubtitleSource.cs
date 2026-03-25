using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellySub.Models;

namespace Jellyfin.Plugin.JellySub.Sources;

/// <summary>
/// Abstraction for a subtitle site scraper.
/// Each implementation targets one website and is independent of the others.
/// </summary>
public interface ISubtitleSource
{
    /// <summary>Stable identifier matching one of the <see cref="SourceIds"/> constants.</summary>
    string Id { get; }

    /// <summary>Display name shown in the settings UI.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Search for subtitles matching the given request.
    /// Must not throw — return an empty list on any error and log the failure.
    /// </summary>
    Task<IReadOnlyList<SubtitleResult>> SearchAsync(
        SubtitleSearchRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Download the raw subtitle content (a .srt string) for the given result.
    /// The caller handles the <see cref="SubtitleResult.DownloadUrl"/>; this method
    /// handles any site-specific redirects, zip extraction, or scraping needed to
    /// reach the actual subtitle text.
    /// </summary>
    Task<string?> DownloadAsync(SubtitleResult result, CancellationToken cancellationToken);
}
