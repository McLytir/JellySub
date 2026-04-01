using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellySub.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySub.Services;

/// <summary>
/// Given the user's chosen subtitle for episode 1, finds the best matching
/// subtitle for every other episode in the series by:
///   1. Same uploader name (exact, case-insensitive)
///   2. Same release-group / encoding tag (partial match)
///   3. Fall back to highest download count
/// </summary>
public sealed class SeriesMatchingService
{
    private readonly SubtitleAggregator _aggregator;
    private readonly ILogger<SeriesMatchingService> _logger;

    /// <summary>Initializes the series subtitle matching service.</summary>
    public SeriesMatchingService(
        SubtitleAggregator aggregator,
        ILogger<SeriesMatchingService> logger)
    {
        _aggregator = aggregator;
        _logger     = logger;
    }

    /// <summary>
    /// For each episode in <paramref name="episodes"/> (all except ep1 whose subtitle
    /// is already set), run a search and pick the best-matching subtitle based on
    /// the anchor uploader/pattern from <paramref name="anchorSubtitle"/>.
    /// </summary>
    public async Task MatchEpisodesAsync(
        IList<EpisodeSubtitleEntry> episodes,
        SubtitleResult anchorSubtitle,
        string language,
        CancellationToken cancellationToken)
    {
        var anchorUploader = anchorSubtitle.Uploader;
        var anchorPattern  = anchorSubtitle.ReleaseGroup;

        _logger.LogInformation(
            "Series matching anchor: uploader='{Uploader}', pattern='{Pattern}'",
            anchorUploader, anchorPattern);

        foreach (var episode in episodes.Where(e => e.ChosenSubtitle is null && !e.AlreadyHasSubtitle))
        {
            try
            {
                var isEpisodeLike =
                    episode.SeasonNumber > 0 &&
                    episode.EpisodeNumber > 0 &&
                    !string.IsNullOrWhiteSpace(episode.SeriesTitle);

                var request = new SubtitleSearchRequest
                {
                    Title         = isEpisodeLike ? episode.SeriesTitle! : episode.SearchTitle,
                    SeriesTitle   = isEpisodeLike ? episode.SeriesTitle : null,
                    SeasonNumber  = isEpisodeLike ? episode.SeasonNumber : null,
                    EpisodeNumber = isEpisodeLike ? episode.EpisodeNumber : null,
                    Languages     = new List<string> { language },
                    MediaFilePath = episode.MediaPath,
                };

                var candidates = await _aggregator
                    .SearchAsync(request, cancellationToken)
                    .ConfigureAwait(false);

                episode.ChosenSubtitle = Pick(
                    candidates,
                    anchorUploader,
                    anchorPattern,
                    out var method);

                episode.MatchMethod = method;

                _logger.LogDebug(
                    "S{S:00}E{E:00} → {Method}: {Name}",
                    episode.SeasonNumber,
                    episode.EpisodeNumber,
                    method,
                    episode.ChosenSubtitle?.ReleaseName ?? "none");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Match failed for S{S}E{E}",
                    episode.SeasonNumber,
                    episode.EpisodeNumber);
                episode.MatchMethod = "NotFound";
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static SubtitleResult? Pick(
        IReadOnlyList<SubtitleResult> candidates,
        string anchorUploader,
        string anchorPattern,
        out string method)
    {
        if (!candidates.Any())
        {
            method = "NotFound";
            return null;
        }

        // 1 — Exact uploader match
        if (!string.IsNullOrWhiteSpace(anchorUploader))
        {
            var byUploader = candidates.FirstOrDefault(c =>
                string.Equals(c.Uploader, anchorUploader, StringComparison.OrdinalIgnoreCase));
            if (byUploader is not null)
            {
                method = "UploaderMatch";
                return byUploader;
            }
        }

        // 2 — Release-group / pattern match
        if (!string.IsNullOrWhiteSpace(anchorPattern))
        {
            var releaseGroup = ExtractReleaseGroup(anchorPattern);
            if (!string.IsNullOrEmpty(releaseGroup))
            {
                var byPattern = candidates.FirstOrDefault(c =>
                    c.ReleaseGroup.Contains(releaseGroup, StringComparison.OrdinalIgnoreCase));
                if (byPattern is not null)
                {
                    method = "PatternMatch";
                    return byPattern;
                }
            }
        }

        // 3 — Best available (already ranked by aggregator: hash → downloads → date)
        method = "BestAvailable";
        return candidates[0];
    }

    private static string ExtractReleaseGroup(string pattern)
    {
        // "1080p.BluRay.x264-GROUP" → "GROUP"
        var m = Regex.Match(pattern, @"-([A-Za-z0-9]+)$");
        return m.Success ? m.Groups[1].Value : string.Empty;
    }
}
