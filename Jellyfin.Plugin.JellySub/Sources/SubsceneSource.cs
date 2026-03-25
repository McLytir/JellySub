using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Jellyfin.Plugin.JellySub.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySub.Sources;

/// <summary>
/// Scrapes subscene.com.
///
/// ⚠ Subscene uses Cloudflare bot protection.  This scraper will work on many
///   self-hosted Jellyfin installs (server IPs are usually not flagged) but may
///   fail if Cloudflare returns a challenge page.  If it stops working, disable
///   this source in the plugin settings and re-enable if the site changes policy.
///
/// HTML notes (2024):
///   Title search : /subtitles/searchbytitle?query={title}
///   Title page   : /subtitles/{slug}
///   Sub row      : table.other-subs tbody tr  →  td.a1 a  + td.a4 span (lang)
///   Download page: /subtitles/{slug}/{langslug}/{sub-id}
///   Zip download : a#downloadButton href
/// </summary>
public sealed class SubsceneSource : ISubtitleSource
{
    private const string BaseUrl = "https://subscene.com";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<SubsceneSource> _logger;

    public SubsceneSource(IHttpClientFactory httpFactory, ILogger<SubsceneSource> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public string Id          => SourceIds.Subscene;
    public string DisplayName => "Subscene";

    // ─────────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SubtitleResult>> SearchAsync(
        SubtitleSearchRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title) && string.IsNullOrWhiteSpace(request.SeriesTitle))
        {
            return Array.Empty<SubtitleResult>();
        }

        try
        {
            var queryTitle = request.SeasonNumber.HasValue
                ? request.SeriesTitle ?? request.Title!
                : request.Title!;

            // Step 1: find the title slug
            var slug = await FindTitleSlugAsync(queryTitle, request, cancellationToken)
                .ConfigureAwait(false);
            if (slug is null) return Array.Empty<SubtitleResult>();

            // Step 2: parse the title's subtitle listing
            var url = $"{BaseUrl}/subtitles/{slug}";
            _logger.LogDebug("[Subscene] GET {Url}", url);
            var html = await FetchHtmlAsync(url, cancellationToken).ConfigureAwait(false);

            return ParseSubtitleList(html, slug, request);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Subscene] Search failed for '{Title}'", request.Title);
            return Array.Empty<SubtitleResult>();
        }
    }

    public async Task<string?> DownloadAsync(SubtitleResult result, CancellationToken cancellationToken)
    {
        try
        {
            // Page contains a single "Download" button whose href is the zip
            var html = await FetchHtmlAsync(result.DownloadUrl, cancellationToken)
                .ConfigureAwait(false);

            var zipLink = ExtractZipLink(html);
            if (zipLink is null) return null;

            if (!zipLink.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                zipLink = BaseUrl + zipLink;
            }

            var client = _httpFactory.CreateClient("JellySub");
            var bytes = await client.GetByteArrayAsync(zipLink, cancellationToken).ConfigureAwait(false);
            return ExtractSrtFromZip(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Subscene] Download failed for {Url}", result.DownloadUrl);
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task<string?> FindTitleSlugAsync(
        string title,
        SubtitleSearchRequest req,
        CancellationToken ct)
    {
        var url = $"{BaseUrl}/subtitles/searchbytitle?query={Uri.EscapeDataString(title)}";
        var html = await FetchHtmlAsync(url, ct).ConfigureAwait(false);

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Results list: ul.search-result > li > div.title > a
        var links = doc.DocumentNode.SelectNodes(
            "//ul[@class='search-result']//div[@class='title']//a");

        if (links is null) return null;

        foreach (var link in links)
        {
            var text = HtmlEntity.DeEntitize(link.InnerText.Trim());
            var href = link.GetAttributeValue("href", string.Empty);

            // Match by year in the title text if available
            if (req.Year.HasValue && text.Contains(req.Year.Value.ToString()))
            {
                return href.TrimStart('/').Replace("subtitles/", string.Empty);
            }
        }

        // Fall back to first result
        var first = links.FirstOrDefault();
        if (first is null) return null;
        var firstHref = first.GetAttributeValue("href", string.Empty);
        return firstHref.TrimStart('/').Replace("subtitles/", string.Empty);
    }

    private List<SubtitleResult> ParseSubtitleList(
        string html,
        string slug,
        SubtitleSearchRequest req)
    {
        var results = new List<SubtitleResult>();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Table rows — Subscene uses <table class="other-subs">
        var rows = doc.DocumentNode.SelectNodes(
            "//table[contains(@class,'other-subs')]//tbody//tr");

        if (rows is null) return results;

        foreach (var row in rows)
        {
            try
            {
                var langNode  = row.SelectSingleNode(".//td[@class='a1']//span[2]");
                var titleNode = row.SelectSingleNode(".//td[@class='a1']//a");
                if (langNode is null || titleNode is null) continue;

                var langLabel = langNode.InnerText.Trim();
                var twoLetter = NormaliseLangLabel(langLabel);

                if (req.Languages.Count > 0 &&
                    !req.Languages.Contains(twoLetter, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var href        = titleNode.GetAttributeValue("href", string.Empty);
                var releaseName = HtmlEntity.DeEntitize(titleNode.InnerText.Trim());
                var subId       = Regex.Match(href, @"-(\d+)$").Groups[1].Value;

                // HI flag
                var isHi = row.InnerHtml.Contains("hearing-impaired", StringComparison.OrdinalIgnoreCase)
                        || releaseName.Contains("HI", StringComparison.Ordinal);

                // Upload date (td.a5 or similar)
                var dateNode = row.SelectSingleNode(".//td[@class='a4']//span");
                DateTime.TryParse(dateNode?.GetAttributeValue("title", string.Empty), out var uploadDate);

                // Uploader
                var uploaderNode = row.SelectSingleNode(".//td[@class='a7']//a");
                var uploader = HtmlEntity.DeEntitize(uploaderNode?.InnerText.Trim() ?? string.Empty);

                results.Add(new SubtitleResult
                {
                    Id            = subId,
                    SourceId      = Id,
                    ReleaseName   = releaseName,
                    Language      = twoLetter,
                    LanguageName  = LanguageMap.DisplayName(twoLetter),
                    DownloadCount = 0,
                    Uploader      = uploader,
                    UploadDate    = uploadDate == default ? null : uploadDate,
                    Format        = "srt",
                    IsHearingImpaired = isHi,
                    DownloadUrl   = BaseUrl + href,
                    ReleaseGroup  = ExtractReleaseGroup(releaseName),
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[Subscene] Row parse error");
            }
        }

        return results
            .Where(r => r.DownloadCount >= req.MinDownloadCount)
            .ToList();
    }

    private static string? ExtractZipLink(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var node = doc.DocumentNode.SelectSingleNode("//*[@id='downloadButton']");
        return node?.GetAttributeValue("href", null);
    }

    private static string NormaliseLangLabel(string label)
    {
        // Subscene language labels are full English names (e.g. "English", "Farsi/Persian")
        var clean = label.Split('/')[0].Trim().ToLowerInvariant();
        return LanguageMap.ToTwoLetter(LanguageMap.ToThreeLetter(clean)) is { } two && two != clean
            ? two
            : clean.Length >= 2 ? clean[..2] : clean;
    }

    private static string ExtractReleaseGroup(string name)
    {
        var m = Regex.Match(name, @"((?:480|576|720|1080|2160)[ip].*)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : string.Empty;
    }

    private async Task<string> FetchHtmlAsync(string url, CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("JellySub");
        var response = await client.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private static string? ExtractSrtFromZip(byte[] bytes)
    {
        using var ms  = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = zip.Entries.FirstOrDefault(e =>
            e.FullName.EndsWith(".srt", StringComparison.OrdinalIgnoreCase));
        if (entry is null) return null;
        using var sr = new StreamReader(entry.Open());
        return sr.ReadToEnd();
    }
}
