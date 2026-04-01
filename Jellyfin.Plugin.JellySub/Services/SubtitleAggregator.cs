using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellySub.Models;
using Jellyfin.Plugin.JellySub.Sources;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySub.Services;

/// <summary>
/// Fans out a search request across all enabled sources (in priority order),
/// merges the results, deduplicates, and returns a ranked list.
/// </summary>
public sealed class SubtitleAggregator
{
    private readonly IEnumerable<ISubtitleSource> _sources;
    private readonly ILogger<SubtitleAggregator> _logger;

    /// <summary>Initializes the subtitle aggregator.</summary>
    public SubtitleAggregator(
        IEnumerable<ISubtitleSource> sources,
        ILogger<SubtitleAggregator> logger)
    {
        _sources = sources;
        _logger  = logger;
    }

    /// <summary>
    /// Run search across all enabled sources concurrently.
    /// </summary>
    public async Task<IReadOnlyList<SubtitleResult>> SearchAsync(
        SubtitleSearchRequest request,
        CancellationToken cancellationToken)
    {
        var cfg            = Plugin.Instance!.Configuration;
        var enabledSources = cfg.EnabledSources;

        // Preserve priority order while running in parallel
        var orderedSources = _sources
            .Where(s => enabledSources.Contains(s.Id))
            .OrderBy(s => enabledSources.IndexOf(s.Id))
            .ToList();

        var tasks = orderedSources.Select(s => SafeSearch(s, request, cancellationToken));
        var allResults = await Task.WhenAll(tasks).ConfigureAwait(false);

        for (var i = 0; i < orderedSources.Count; i++)
        {
            _logger.LogInformation(
                "Aggregator source {Source} returned {Count} results",
                orderedSources[i].Id,
                allResults[i].Count);
        }

        var merged = allResults.SelectMany(r => r).ToList();

        // Apply global minimum download-count filter
        if (cfg.MinimumDownloadCount > 0)
        {
            merged = merged
                .Where(r => r.DownloadCount >= cfg.MinimumDownloadCount)
                .ToList();
        }

        _logger.LogInformation(
            "Aggregator merged {Count} results after filtering (MinDownloadCount={MinDownloadCount})",
            merged.Count,
            cfg.MinimumDownloadCount);

        return Rank(merged);
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<SubtitleResult>> SafeSearch(
        ISubtitleSource source,
        SubtitleSearchRequest request,
        CancellationToken ct)
    {
        try
        {
            return await source.SearchAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Source {Source} threw during search", source.Id);
            return Array.Empty<SubtitleResult>();
        }
    }

    private static List<SubtitleResult> Rank(List<SubtitleResult> results)
    {
        return results
            .OrderByDescending(r => r.IsHashMatch)
            .ThenByDescending(r => r.DownloadCount)
            .ThenByDescending(r => r.UploadDate)
            .ThenBy(r => r.IsMachineTranslated)
            .ToList();
    }
}
