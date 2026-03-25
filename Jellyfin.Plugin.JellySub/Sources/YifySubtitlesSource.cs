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
/// Scrapes yifysubtitles.ch — best suited for movies (indexed by IMDb ID).
///
/// HTML notes (2024):
///   Movie page : /movie-imdb/tt{imdbid}
///   Sub rows   : div.other-subs  →  div.row
///     Language : span.label (text content)
///     Title    : a[href^="/subtitle/"] (text content)
///     Download : a[href^="/subtitle/"] → follow page → a.subtitle-download
///   Download   : /subtitle/{id}  page contains a download link
/// </summary>
public sealed class YifySubtitlesSource : ISubtitleSource
{
    private const string BaseUrl = "https://yifysubtitles.ch";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<YifySubtitlesSource> _logger;

    // Map site language labels to BCP-47
    private static readonly Dictionary<string, string> LabelToCode =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["english"] = "en",  ["french"]   = "fr", ["german"]   = "de",
            ["spanish"] = "es",  ["italian"]  = "it", ["portuguese"] = "pt",
            ["russian"] = "ru",  ["japanese"] = "ja", ["korean"]   = "ko",
            ["chinese"] = "zh",  ["arabic"]   = "ar", ["dutch"]    = "nl",
            ["polish"]  = "pl",  ["swedish"]  = "sv", ["norwegian"] = "no",
            ["danish"]  = "da",  ["finnish"]  = "fi", ["czech"]    = "cs",
            ["hungarian"] = "hu",["romanian"] = "ro", ["turkish"]  = "tr",
            ["greek"]   = "el",  ["hebrew"]   = "he", ["ukrainian"] = "uk",
            ["bulgarian"] = "bg",["croatian"] = "hr", ["serbian"]  = "sr",
        };

    public YifySubtitlesSource(IHttpClientFactory httpFactory, ILogger<YifySubtitlesSource> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public string Id          => SourceIds.YifySubtitles;
    public string DisplayName => "YifySubtitles";

    // ─────────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SubtitleResult>> SearchAsync(
        SubtitleSearchRequest request,
        CancellationToken cancellationToken)
    {
        // YifySubtitles only works for movies and requires an IMDb ID
        if (string.IsNullOrWhiteSpace(request.ImdbId) || request.SeasonNumber.HasValue)
        {
            return Array.Empty<SubtitleResult>();
        }

        try
        {
            var imdbNumeric = request.ImdbId.TrimStart('t');
            var url = $"{BaseUrl}/movie-imdb/tt{imdbNumeric}";

            _logger.LogDebug("[Yify] GET {Url}", url);
            var html = await FetchHtmlAsync(url, cancellationToken).ConfigureAwait(false);
            var results = ParseResultsPage(html, request);

            return results
                .Where(r => r.DownloadCount >= request.MinDownloadCount)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Yify] Search failed for IMDb {Id}", request.ImdbId);
            return Array.Empty<SubtitleResult>();
        }
    }

    public async Task<string?> DownloadAsync(SubtitleResult result, CancellationToken cancellationToken)
    {
        try
        {
            // The DownloadUrl points to the subtitle detail page; scrape the actual zip link
            var html = await FetchHtmlAsync(result.DownloadUrl, cancellationToken).ConfigureAwait(false);
            var downloadLink = ExtractActualDownloadLink(html);
            if (downloadLink is null) return null;

            if (!downloadLink.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                downloadLink = BaseUrl + downloadLink;
            }

            var client = _httpFactory.CreateClient("JellySub");
            var bytes = await client.GetByteArrayAsync(downloadLink, cancellationToken).ConfigureAwait(false);
            return ExtractSrtFromZip(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Yify] Download failed for {Url}", result.DownloadUrl);
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task<string> FetchHtmlAsync(string url, CancellationToken ct)
    {
        var client = _httpFactory.CreateClient("JellySub");
        var response = await client.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    private List<SubtitleResult> ParseResultsPage(string html, SubtitleSearchRequest req)
    {
        var results = new List<SubtitleResult>();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var rows = doc.DocumentNode.SelectNodes(
            "//div[contains(@class,'other-subs')]//div[contains(@class,'row') and not(contains(@class,'head'))]");

        if (rows is null) return results;

        foreach (var row in rows)
        {
            try
            {
                // Language label
                var langNode = row.SelectSingleNode(".//span[contains(@class,'label')]");
                if (langNode is null) continue;
                var langLabel = langNode.InnerText.Trim();
                if (!LabelToCode.TryGetValue(langLabel, out var twoLetter))
                {
                    twoLetter = langLabel.ToLowerInvariant().Length >= 2
                        ? langLabel.Substring(0, 2).ToLowerInvariant()
                        : langLabel;
                }

                // Language filter
                if (req.Languages.Count > 0 &&
                    !req.Languages.Contains(twoLetter, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Title / sub page link
                var linkNode = row.SelectSingleNode(".//a[starts-with(@href,'/subtitle/')]");
                if (linkNode is null) continue;

                var subPageHref = linkNode.GetAttributeValue("href", string.Empty);
                var releaseName = HtmlEntity.DeEntitize(linkNode.InnerText.Trim());

                // Derive an ID from the subtitle page path
                var subId = Regex.Match(subPageHref, @"/subtitle/([^/]+)").Groups[1].Value;

                // SDH flag
                var isHi = row.InnerHtml.Contains("hi-subtitle", StringComparison.OrdinalIgnoreCase);

                results.Add(new SubtitleResult
                {
                    Id           = subId,
                    SourceId     = Id,
                    ReleaseName  = releaseName,
                    Language     = twoLetter,
                    LanguageName = LanguageMap.DisplayName(twoLetter),
                    DownloadCount = 0,        // Yify doesn't expose download counts
                    Uploader     = string.Empty,
                    Format       = "srt",
                    IsHearingImpaired = isHi,
                    DownloadUrl  = BaseUrl + subPageHref,
                    ReleaseGroup = ExtractReleaseGroup(releaseName),
                });
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[Yify] Row parse error");
            }
        }

        return results;
    }

    private static string? ExtractActualDownloadLink(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // <a class="subtitle-download" href="...">
        var node = doc.DocumentNode.SelectSingleNode(
            "//a[contains(@class,'subtitle-download')]");
        return node?.GetAttributeValue("href", null);
    }

    private static string ExtractReleaseGroup(string name)
    {
        var m = Regex.Match(name, @"((?:480|576|720|1080|2160)[ip].*)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : string.Empty;
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
