using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using HtmlAgilityPack;
using Jellyfin.Plugin.JellySub.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySub.Sources
{
    /// <summary>
    /// Scrapes opensubtitles.org (the classic site) — no account required.
    /// Implements the same logic as the subtitle-finder Node.js project.
    /// </summary>
    public sealed class OpenSubtitlesOrgSource : ISubtitleSource
    {
        private const string BaseUrl = "https://www.opensubtitles.org";
        private const string SearchUrl = BaseUrl + "/en/search";
        private const int PageSize = 40;

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
            var results = new List<SubtitleResult>();

            // Build one query per requested language (site accepts one lang at a time)
            var languages = request.Languages.Count > 0
                ? request.Languages
                : new List<string> { "en" };

            foreach (var lang in languages)
            {
                try
                {
                    int page = 1;
                    bool hasMore = true;

                    while (hasMore && results.Count < 100) // Limit to prevent excessive requests
                    {
                        var url = BuildSearchUrl(request.Title ?? request.SeriesTitle ?? string.Empty, lang, page);
                        _logger.LogDebug("[OpenSubtitlesOrg] GET {Url}", url);
                        var html = await FetchHtmlAsync(url, cancellationToken).ConfigureAwait(false);
                        var parsed = ParseResultsPage(html, lang, out var paginationInfo);
                        results.AddRange(parsed);

                        hasMore = paginationInfo.HasMore;
                        page = paginationInfo.NextPage ?? page + 1;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[OpenSubtitlesOrg] Search failed for language {Lang}", lang);
                }
            }

            // Sort results: exact match boost, then subtitle count, then title
            results = results.OrderByDescending(r => IsExactMatch(r.ReleaseName, request.Title ?? request.SeriesTitle ?? string.Empty))
                             .ThenByDescending(r => r.DownloadCount)
                             .ThenBy(r => r.ReleaseName)
                             .ToList();

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
        // Helpers (mirroring subtitle-finder Node.js logic)
        // ─────────────────────────────────────────────────────────────────────────

        private static string BuildSearchUrl(string query, string language, int page)
        {
            // Node.js: const normalizedQuery = encodeURIComponent(query.trim());
            var normalizedQuery = Uri.EscapeDataString(query.Trim());
            // Node.js: const languageSegment = language === 'all' ? '' : `/sublanguageid-${language}`;
            var languageSegment = language.Equals("all", StringComparison.OrdinalIgnoreCase) ? "" : $"/sublanguageid-{language}";
            // Node.js: const base = `${BASE_URL}/en/search${languageSegment}/moviename-${normalizedQuery}`;
            var baseUrl = $"{SearchUrl}{languageSegment}/moviename-{normalizedQuery}";
            if (page <= 1)
                return baseUrl;
            // Node.js: return `${base}/offset-${(page - 1) * PAGE_SIZE}`;
            return $"{baseUrl}/offset-{(page - 1) * PageSize}";
        }

        private async Task<string> FetchHtmlAsync(string url, CancellationToken ct)
        {
            var client = _httpFactory.CreateClient("JellySub");
            // Mimic browser Accept header to avoid 406 responses
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            // Set a reasonable user agent
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            var response = await client.GetAsync(url, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }

        private List<SubtitleResult> ParseResultsPage(string html, string lang, out PaginationInfo paginationInfo)
        {
            var results = new List<SubtitleResult>();
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Each result row in the search table: tr.change or tr.expandable within #search_results
            var rows = doc.DocumentNode.SelectNodes(
                "//table[@id='search_results']//tr[contains(@class,'change') or contains(@class,'expandable')]");

            if (rows is null)
            {
                paginationInfo = new PaginationInfo { HasMore = false, NextPage = null };
                return results;
            }

            foreach (var row in rows)
            {
                try
                {
                    var parsed = ParseSubtitleRow(row);
                    if (parsed == null) continue;

                    // Override language with the requested lang (converted to 2-letter via LanguageMap)
                    var twoLetter = LanguageMap.ToTwoLetter(lang.ToLowerInvariant());
                    parsed.Language = twoLetter;
                    parsed.LanguageName = LanguageMap.DisplayName(twoLetter);

                    results.Add(parsed);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[OpenSubtitlesOrg] Failed to parse a result row");
                }
            }

            paginationInfo = ParseHasMore(html);
            return results;
        }

        private SubtitleResult? ParseSubtitleRow(HtmlNode row)
        {
            var cells = row.SelectNodes("td");
            if (cells == null || cells.Count < 5)
                return null;

            // --- Title / ID ---
            var titleCell = cells[0];
            var titleLink = titleCell.SelectSingleNode(".//a[contains(@class,'bnone')]");
            if (titleLink == null)
                return null;

            var href = titleLink.GetAttributeValue("href", string.Empty);
            var subtitleId = ExtractSubtitleId(href);
            if (string.IsNullOrEmpty(subtitleId))
                return null;

            var rawTitle = titleLink.InnerText.Trim();
            var titleMatch = Regex.Match(rawTitle, @"^(.*?)(?:\s*\((\d{4})\))?$");
            var title = titleMatch.Success ? titleMatch.Groups[1].Value.Trim() : rawTitle;
            var year = titleMatch.Success ? titleMatch.Groups[2].Value : string.Empty;

            var movieId = $"{Slugify(title)}-{year}".TrimEnd('-');

            // --- Release name ---
            var releaseNameSpan = titleCell.SelectSingleNode(".//span[@title]");
            var releaseName = releaseNameSpan != null
                ? HttpEntity.DeEntitize(releaseNameSpan.GetAttributeValue("title", string.Empty).Trim())
                : HttpEntity.DeEntitize(titleLink.InnerText.Trim());

            // --- Download count ---
            var dlCount = 0;
            var dlCell = cells[4];
            var dlLink = dlCell.SelectSingleNode(".//a");
            if (dlLink != null && int.TryParse(dlLink.InnerText.Replace(",", "").Trim(), out var parsedDlCount))
            {
                dlCount = parsedDlCount;
            }

            // --- CDs (from cell index 2) ---
            var cds = cells[2].InnerText.Trim();

            // --- Uploaded at and FPS (from cell index 3) ---
            var dateCell = cells[3];
            var uploadedAt = string.Empty;
            var fps = string.Empty;

            var timeNode = dateCell.SelectSingleNode(".//time");
            if (timeNode != null)
            {
                uploadedAt = timeNode.InnerText.Trim();
            }

            var fpsSpan = dateCell.SelectSingleNode(".//span[contains(@class,'p')]");
            if (fpsSpan != null)
            {
                fps = fpsSpan.InnerText.Trim();
            }
            else
            {
                // Fallback to first span if no p class
                var firstSpan = dateCell.SelectSingleNode(".//span");
                if (firstSpan != null)
                {
                    fps = firstSpan.InnerText.Trim();
                }
            }

            // --- Format ---
            var formatText = string.Empty;
            var formatSpan = dlCell.SelectSingleNode(".//span[contains(@class,'p')]");
            if (formatSpan != null)
            {
                formatText = formatSpan.InnerText.Trim();
            }
            else
            {
                // Fallback: take the cell text and remove the download count
                var cellText = HttpEntity.DeEntitize(dlCell.InnerText.Trim());
                formatText = cellText.Replace(dlCount.ToString(), "").Trim();
            }

            if (formatText.Contains(' '))
            {
                var parts = formatText.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                formatText = parts[parts.Length - 1];
            }

            // --- Rating ---
            var rating = cells.Count > 5 ? cells[5].InnerText.Trim() : string.Empty;

            // --- Language from img alt ---
            var langImg = row.SelectSingleNode(".//td[contains(@class,'a4')]//img");
            var langRaw = langImg?.GetAttributeValue("alt", string.Empty) ?? string.Empty;
            // langRaw is the 3-letter code from the site; we will override later in ParseResultsPage
            // Keep it for now; we'll override with requested language.

            // --- Detail URL and Download URL ---
            var detailUrl = string.Empty;
            if (!string.IsNullOrEmpty(href))
            {
                detailUrl = new Uri(new Uri(BaseUrl), href).ToString();
            }

            var downloadUrl = string.Empty;
            if (!string.IsNullOrEmpty(subtitleId))
            {
                downloadUrl = $"https://dl.opensubtitles.org/en/download/sub/{subtitleId}";
            }

            return new SubtitleResult
            {
                Id = subtitleId,
                SourceId = Id,
                ReleaseName = releaseName,
                Language = string.Empty, // will be overridden
                LanguageName = string.Empty,
                DownloadCount = dlCount,
                Uploader = string.Empty, // Not extracted in the new logic; left empty
                UploadDate = null, // Not parsed in the new logic; left null
                Format = formatText,
                Rating = rating,
                DetailUrl = detailUrl,
                DownloadUrl = downloadUrl,
                ReleaseGroup = ExtractReleaseGroup(releaseName)
            };
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
            using var ms = new MemoryStream(zipBytes);
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

        private static bool IsExactMatch(string releaseName, string query)
        {
            if (string.IsNullOrEmpty(query))
                return false;

            var normalizedRelease = NormalizeForMatch(releaseName);
            var normalizedQuery = NormalizeForMatch(query);
            return normalizedRelease == normalizedQuery ||
                   normalizedRelease == $"the {normalizedQuery}";
        }

        private static string NormalizeWhitespace(string value)
        {
            if (value == null) return string.Empty;
            return Regex.Replace(value, @"\s+", " ").Trim();
        }

        private static string NormalizeForMatch(string value)
        {
            if (value == null) return string.Empty;
            var normalized = NormalizeWhitespace(value).ToLowerInvariant();
            return Regex.Replace(normalized, @"[^a-z0-9 ]", "");
        }

        private static string Slugify(string value)
        {
            if (value == null) return string.Empty;
            var normalized = NormalizeWhitespace(value).ToLowerInvariant();
            var slug = Regex.Replace(normalized, @"[^a-z0-9]+", "-");
            return slug.TrimStart('-').TrimEnd('-');
        }

        private PaginationInfo ParseHasMore(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var nextRel = doc.DocumentNode.SelectSingleNode("//a[@rel='next']");
            var nextText = doc.DocumentNode.SelectNodes("//a")
                ?.FirstOrDefault(a => Regex.IsMatch(a.InnerText, @"next|>>", RegexOptions.IgnoreCase));

            var nextLink = nextRel ?? nextText;
            if (nextLink == null)
                return new PaginationInfo { HasMore = false, NextPage = null };

            var href = nextLink.GetAttributeValue("href", string.Empty);
            var offsetMatch = Regex.Match(href, @"/offset-(\d+)");
            if (offsetMatch.Success)
            {
                var offset = int.Parse(offsetMatch.Groups[1].Value);
                return new PaginationInfo
                {
                    HasMore = true,
                    NextPage = offset / PageSize + 1
                };
            }

            return new PaginationInfo { HasMore = true, NextPage = null }; // fallback
        }

        private class PaginationInfo
        {
            public bool HasMore { get; set; }
            public int? NextPage { get; set; }
        }
    }
}