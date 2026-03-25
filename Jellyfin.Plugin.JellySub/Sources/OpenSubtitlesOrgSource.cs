using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using HtmlAgilityPack;
using Jellyfin.Plugin.JellySub.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySub.Sources;

/// <summary>
/// Scrapes opensubtitles.org (the classic site) — no account required.
///
/// HTML structure notes (correct as of 2024; selectors may need updating if the
/// site is redesigned):
///   Search URL  : /en/search2/sublanguageid-{lang}/moviename-{title}[/season-N/episode-N]
///   Results     : table#search_results → tbody → tr.odd / tr.even
///   Title link  : td.a1 a.bnone  →  href="/en/subtitles/{id}/..."
///   Download cnt: td.a5
///   Uploader    : td.a7 a
///   Upload date : td.a6
///   Language img: td.a4 img[alt]
///   Download URL: /en/subtitleserve/sub/{id}  (returns a zip)
/// </summary>
public sealed class OpenSubtitlesOrgSource : ISubtitleSource
{
    private const string BaseUrl  = "https://www.opensubtitles.org";
    private const string SearchUrl = BaseUrl + "/en/search2";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<OpenSubtitlesOrgSource> _logger;

    public OpenSubtitlesOrgSource(IHttpClientFactory httpFactory, ILogger<OpenSubtitlesOrgSource> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public string Id          => SourceIds.OpenSubtitlesOrg;
    public string DisplayName => "OpenSubtitles.org";

    // ─────────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SubtitleResult>> SearchAsync(
        SubtitleSearchRequest request,
        CancellationToken cancellationToken)
    {
        var results = new List<SubtitleResult>();

        // Build one query per requested language (site accepts one lang at a time)
        var languages = request.Languages.Count > 0
            ? request.Languages
            : new List<string> { "en" };

        foreach (var lang in languages)
        {
            try
            {
                var url = BuildSearchUrl(request, lang);
                _logger.LogDebug("[OpenSubtitlesOrg] GET {Url}", url);
                var html = await FetchHtmlAsync(url, cancellationToken).ConfigureAwait(false);
                var parsed = ParseResultsPage(html, lang);
                results.AddRange(parsed);
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
            var response = await client.GetAsync(result.DownloadUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

            // The response is always a zip archive
            return ExtractSrtFromZip(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[OpenSubtitlesOrg] Download failed for {Url}", result.DownloadUrl);
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static string BuildSearchUrl(SubtitleSearchRequest req, string lang)
    {
        var threeLetter = LanguageMap.ToThreeLetter(lang);
        var title = req.SeasonNumber.HasValue
            ? req.SeriesTitle ?? req.Title ?? string.Empty
            : req.Title ?? string.Empty;

        // HttpUtility.UrlEncode uses '+' for spaces; replace with '%20' for URLs
        var encoded = HttpUtility.UrlEncode(title.ToLowerInvariant()).Replace("+", "%20");

        var url = $"{SearchUrl}/sublanguageid-{threeLetter}/moviename-{encoded}";

        if (req.SeasonNumber.HasValue)
        {
            url += $"/season-{req.SeasonNumber.Value}";
        }

        if (req.EpisodeNumber.HasValue)
        {
            url += $"/episode-{req.EpisodeNumber.Value}";
        }

        return url + "/offset-0";
    }

    private async Task<string> FetchHtmlAsync(string url, CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("JellySub");
        // Mimic browser Accept header to avoid 406 responses
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        var response = await client.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private List<SubtitleResult> ParseResultsPage(string html, string lang)
    {
        var results = new List<SubtitleResult>();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Each result row in the search table
        var rows = doc.DocumentNode.SelectNodes(
            "//table[@id='search_results']//tr[contains(@class,'odd') or contains(@class,'even')]");

        if (rows is null)
        {
            return results;
        }

        foreach (var row in rows)
        {
            try
            {
                // ── Title / ID ──────────────────────────────────────────────
                var titleNode = row.SelectSingleNode(".//td[contains(@class,'a1')]//a[contains(@class,'bnone')]");
                if (titleNode is null) continue;

                var href = titleNode.GetAttributeValue("href", string.Empty);
                var subtitleId = ExtractSubtitleId(href);
                if (string.IsNullOrEmpty(subtitleId)) continue;

                var releaseName = HtmlEntity.DeEntitize(titleNode.InnerText.Trim());

                // ── Download count ──────────────────────────────────────────
                var dlNode = row.SelectSingleNode(".//td[contains(@class,'a5')]");
                int.TryParse(dlNode?.InnerText.Replace(",", "").Trim(), out var dlCount);

                // ── Uploader ────────────────────────────────────────────────
                var uploaderNode = row.SelectSingleNode(".//td[contains(@class,'a7')]//a");
                var uploader = HtmlEntity.DeEntitize(uploaderNode?.InnerText.Trim() ?? string.Empty);

                // ── Upload date ─────────────────────────────────────────────
                var dateNode = row.SelectSingleNode(".//td[contains(@class,'a6')]");
                DateTime.TryParse(dateNode?.InnerText.Trim(), out var uploadDate);

                // ── Language from img alt ───────────────────────────────────
                var langImg = row.SelectSingleNode(".//td[contains(@class,'a4')]//img");
                var langRaw = langImg?.GetAttributeValue("alt", lang) ?? lang;
                // langRaw is the 3-letter code from the site; convert to 2-letter
                var twoLetter = LanguageMap.ToTwoLetter(langRaw.ToLowerInvariant());

                results.Add(new SubtitleResult
                {
                    Id           = subtitleId,
                    SourceId     = Id,
                    ReleaseName  = releaseName,
                    Language     = twoLetter,
                    LanguageName = LanguageMap.DisplayName(twoLetter),
                    DownloadCount = dlCount,
                    Uploader     = uploader,
                    UploadDate   = uploadDate == default ? null : uploadDate,
                    Format       = "srt",
                    DownloadUrl  = $"{BaseUrl}/en/subtitleserve/sub/{subtitleId}",
                    ReleaseGroup = ExtractReleaseGroup(releaseName),
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[OpenSubtitlesOrg] Failed to parse a result row");
            }
        }

        return results;
    }

    private static string ExtractSubtitleId(string href)
    {
        // href pattern: /en/subtitles/1234567/title-en
        var match = Regex.Match(href, @"/subtitles/(\d+)/");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string ExtractReleaseGroup(string releaseName)
    {
        // Common pattern: "ShowName.S01E01.1080p.BluRay.x264-GROUP"  →  "1080p.BluRay.x264-GROUP"
        // Capture everything from a known quality tag onward
        var match = Regex.Match(releaseName,
            @"((?:480|576|720|1080|2160)[ip].*)",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string? ExtractSrtFromZip(byte[] zipBytes)
    {
        using var ms  = new MemoryStream(zipBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        // Prefer .srt; fall back to first subtitle-like entry
        var entry = zip.Entries.FirstOrDefault(e =>
                        e.FullName.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
                 ?? zip.Entries.FirstOrDefault(e =>
                        e.FullName.EndsWith(".sub", StringComparison.OrdinalIgnoreCase));

        if (entry is null) return null;

        using var sr = new StreamReader(entry.Open());
        return sr.ReadToEnd();
    }
}
