using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellySub.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySub.Sources;

/// <summary>
/// OpenSubtitles search source backed by the public REST API.
///
/// The classic HTML site is protected by anti-bot challenges, so this source
/// uses the public JSON API instead:
///   - search/imdbid-{ttid}/sublanguageid-{lang}
///   - search/query-{title}/sublanguageid-{lang}
///
/// Downloads are returned as either ZIP or GZip payloads, depending on which
/// API link is used.
/// </summary>
public sealed class OpenSubtitlesOrgSource : ISubtitleSource
{
    private const string BaseUrl = "https://rest.opensubtitles.org";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<OpenSubtitlesOrgSource> _logger;

    public OpenSubtitlesOrgSource(IHttpClientFactory httpFactory, ILogger<OpenSubtitlesOrgSource> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public string Id => SourceIds.OpenSubtitlesOrg;
    public string DisplayName => "OpenSubtitles.org";

    public async Task<IReadOnlyList<SubtitleResult>> SearchAsync(
        SubtitleSearchRequest request,
        CancellationToken cancellationToken)
    {
        var languages = request.Languages.Count > 0
            ? request.Languages
            : new List<string> { "en" };

        var results = new List<SubtitleResult>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var lang in languages)
        {
            try
            {
                var langResults = await SearchLanguageAsync(request, lang, cancellationToken).ConfigureAwait(false);
                foreach (var result in langResults)
                {
                    if (seenIds.Add($"{result.SourceId}:{result.Id}"))
                    {
                        results.Add(result);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[OpenSubtitlesOrg] Search failed for language {Lang}", lang);
            }
        }

        return results
            .Where(r => r.DownloadCount >= request.MinDownloadCount)
            .ToList();
    }

    public async Task<string?> DownloadAsync(SubtitleResult result, CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpFactory.CreateClient("JellySub");
            using var req = new HttpRequestMessage(HttpMethod.Get, result.DownloadUrl);
            req.Headers.TryAddWithoutValidation("Accept", "application/octet-stream, application/zip, text/plain;q=0.9, */*;q=0.8");

            using var response = await client.SendAsync(req, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return ExtractSubtitleText(bytes, result.DownloadUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OpenSubtitlesOrg] Download failed for {Url}", result.DownloadUrl);
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Search helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<SubtitleResult>> SearchLanguageAsync(
        SubtitleSearchRequest request,
        string lang,
        CancellationToken ct)
    {
        var results = new List<SubtitleResult>();

        // Prefer exact IMDb lookups when available; fall back to title search.
        if (!string.IsNullOrWhiteSpace(request.ImdbId))
        {
            var imdbResults = await FetchResultsAsync(
                BuildImdbSearchUrl(request.ImdbId, lang), request, ct).ConfigureAwait(false);
            results.AddRange(imdbResults);

            if (results.Count > 0)
            {
                return results;
            }
        }

        var title = BuildSearchTitle(request);
        if (string.IsNullOrWhiteSpace(title))
        {
            return results;
        }

        results.AddRange(await FetchResultsAsync(BuildTitleSearchUrl(title, lang), request, ct).ConfigureAwait(false));
        return results;
    }

    private async Task<IReadOnlyList<SubtitleResult>> FetchResultsAsync(
        string url,
        SubtitleSearchRequest request,
        CancellationToken ct)
    {
        _logger.LogDebug("[OpenSubtitlesOrg] GET {Url}", url);

        var json = await FetchJsonAsync(url, ct).ConfigureAwait(false);
        var parsed = ParseResults(json, request);

        return parsed;
    }

    private async Task<string> FetchJsonAsync(string url, CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("JellySub");
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*; q=0.01");
        req.Headers.TryAddWithoutValidation("X-User-Agent", "JellySub/1.0");

        using var response = await client.SendAsync(req, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private static string BuildImdbSearchUrl(string imdbId, string lang)
    {
        return $"{BaseUrl}/search/imdbid-{NormalizeImdbId(imdbId)}/sublanguageid-{LanguageMap.ToThreeLetter(lang)}";
    }

    private static string BuildTitleSearchUrl(string title, string lang)
    {
        return $"{BaseUrl}/search/query-{Uri.EscapeDataString(title)}/sublanguageid-{LanguageMap.ToThreeLetter(lang)}";
    }

    private static string BuildSearchTitle(SubtitleSearchRequest request)
    {
        var title = request.SeasonNumber.HasValue
            ? request.SeriesTitle ?? request.Title
            : request.Title;

        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        if (request.SeasonNumber.HasValue)
        {
            return title.Trim();
        }

        return request.Year.HasValue
            ? $"{title.Trim()} {request.Year.Value}"
            : title.Trim();
    }

    private List<SubtitleResult> ParseResults(string json, SubtitleSearchRequest request)
    {
        var results = new List<SubtitleResult>();
        var root = JsonNode.Parse(json) as JsonArray;
        if (root is null)
        {
            return results;
        }

        foreach (var node in root.OfType<JsonObject>())
        {
            try
            {
                var result = MapResult(node, request);
                if (result is not null)
                {
                    results.Add(result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[OpenSubtitlesOrg] Failed to parse a result row");
            }
        }

        return results;
    }

    private SubtitleResult? MapResult(JsonObject item, SubtitleSearchRequest request)
    {
        var languageCode = FirstNonEmpty(item["ISO639"]?.ToString(), item["SubLanguageID"]?.ToString(), item["LanguageName"]?.ToString());
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return null;
        }

        var twoLetter = LanguageMap.ToTwoLetter(languageCode.ToLowerInvariant());
        if (request.Languages.Count > 0 &&
            !request.Languages.Contains(twoLetter, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var season = GetInt(item, "SeriesSeason");
        var episode = GetInt(item, "SeriesEpisode");
        if (request.SeasonNumber.HasValue)
        {
            if (season.GetValueOrDefault() <= 0 || season.Value != request.SeasonNumber.Value)
            {
                return null;
            }

            if (request.EpisodeNumber.HasValue &&
                (episode.GetValueOrDefault() <= 0 || episode.Value != request.EpisodeNumber.Value))
            {
                return null;
            }
        }

        var movieYear = GetInt(item, "MovieYear");
        if (!request.SeasonNumber.HasValue && request.Year.HasValue && movieYear.HasValue && movieYear.Value != request.Year.Value)
        {
            return null;
        }

        var subtitleId = FirstNonEmpty(item["IDSubtitle"]?.ToString(), item["IDSubtitleFile"]?.ToString());
        if (string.IsNullOrWhiteSpace(subtitleId))
        {
            return null;
        }

        var releaseName = FirstNonEmpty(
            item["MovieReleaseName"]?.ToString(),
            item["SubFileName"]?.ToString(),
            item["MovieName"]?.ToString(),
            subtitleId);

        var downloadUrl = FirstNonEmpty(item["ZipDownloadLink"]?.ToString(), item["SubDownloadLink"]?.ToString());
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return null;
        }

        var downloadCount = GetInt(item, "SubDownloadsCnt") ?? 0;
        var uploadDate = GetDateTime(item, "SubAddDate");
        var matchedBy = item["MatchedBy"]?.ToString() ?? string.Empty;
        var releaseGroup = FirstNonEmpty(item["InfoReleaseGroup"]?.ToString(), ExtractReleaseGroup(releaseName));

        return new SubtitleResult
        {
            Id = subtitleId,
            SourceId = Id,
            ReleaseName = releaseName,
            Language = twoLetter,
            LanguageName = LanguageMap.DisplayName(twoLetter),
            DownloadCount = downloadCount,
            Uploader = item["UserNickName"]?.ToString() ?? string.Empty,
            UploadDate = uploadDate,
            Format = item["SubFormat"]?.ToString() ?? "srt",
            DownloadUrl = downloadUrl,
            ReleaseGroup = releaseGroup,
            IsHashMatch = matchedBy.Equals("moviehash", StringComparison.OrdinalIgnoreCase),
            IsHearingImpaired = GetBool(item, "SubHearingImpaired"),
            IsMachineTranslated = GetBool(item, "SubAutoTranslation"),
        };
    }

    private static int? GetInt(JsonObject item, string key)
    {
        var value = item[key]?.ToString();
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    private static DateTime? GetDateTime(JsonObject item, string key)
    {
        var value = item[key]?.ToString();
        return DateTime.TryParse(value, out var parsed) ? parsed : null;
    }

    private static bool GetBool(JsonObject item, string key)
    {
        var value = item[key]?.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

    private static string NormalizeImdbId(string imdbId)
    {
        var trimmed = imdbId.Trim();
        return trimmed.StartsWith("tt", StringComparison.OrdinalIgnoreCase)
            ? trimmed.ToLowerInvariant()
            : $"tt{trimmed.TrimStart('t').TrimStart('T')}";
    }

    private static string ExtractReleaseGroup(string releaseName)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            releaseName,
            @"((?:480|576|720|1080|2160)[ip].*)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string? ExtractSubtitleText(byte[] bytes, string? downloadUrl)
    {
        if (LooksLikeZip(bytes, downloadUrl))
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

                var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
                         ?? zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".sub", StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                {
                    return null;
                }

                using var sr = new StreamReader(entry.Open(), Encoding.UTF8, true);
                return sr.ReadToEnd();
            }
            catch
            {
                // fall through to gzip attempt
            }
        }

        if (LooksLikeGzip(bytes, downloadUrl))
        {
            using var ms = new MemoryStream(bytes);
            using var gzip = new GZipStream(ms, CompressionMode.Decompress);
            using var sr = new StreamReader(gzip, Encoding.UTF8, true);
            return sr.ReadToEnd();
        }

        return null;
    }

    private static bool LooksLikeZip(byte[] bytes, string? downloadUrl)
        => bytes.Length >= 4
        && bytes[0] == 0x50
        && bytes[1] == 0x4B
        && (downloadUrl?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ?? false || bytes[2] == 0x03 || bytes[2] == 0x05 || bytes[2] == 0x07);

    private static bool LooksLikeGzip(byte[] bytes, string? downloadUrl)
        => bytes.Length >= 2
        && bytes[0] == 0x1F
        && bytes[1] == 0x8B
        || (downloadUrl?.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ?? false);
}
