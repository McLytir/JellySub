using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellySub.Api.Dto;
using Jellyfin.Plugin.JellySub.Models;
using Jellyfin.Plugin.JellySub.Services;
using Jellyfin.Plugin.JellySub.Sources;
using Jellyfin.Plugin.JellySub.Sync;
using Jellyfin.Plugin.JellySub.Tasks;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySub.Api;

/// <summary>
/// JellySub REST API consumed by the embedded web pages.
/// All endpoints require Jellyfin authentication.
/// </summary>
[ApiController]
[Produces(MediaTypeNames.Application.Json)]
[Authorize]
[Route("JellySub")]
public sealed class JellySubController : ControllerBase
{
    private readonly ILibraryManager _libraryManager;
    private readonly SubtitleAggregator _aggregator;
    private readonly SubtitleFileService _fileService;
    private readonly SeriesMatchingService _seriesMatcher;
    private readonly SyncToolManager _syncManager;
    private readonly ITaskManager _taskManager;
    private readonly IEnumerable<ISubtitleSource> _sources;
    private readonly ILogger<JellySubController> _logger;

    public JellySubController(
        ILibraryManager libraryManager,
        SubtitleAggregator aggregator,
        SubtitleFileService fileService,
        SeriesMatchingService seriesMatcher,
        SyncToolManager syncManager,
        ITaskManager taskManager,
        IEnumerable<ISubtitleSource> sources,
        ILogger<JellySubController> logger)
    {
        _libraryManager = libraryManager;
        _aggregator     = aggregator;
        _fileService    = fileService;
        _seriesMatcher  = seriesMatcher;
        _syncManager    = syncManager;
        _taskManager    = taskManager;
        _sources        = sources;
        _logger         = logger;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SEARCH
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Assisted search: resolve metadata from a Jellyfin item, then search all
    /// enabled sources.  Returns a ranked result list for user selection.
    /// </summary>
    [HttpGet("search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<SearchResultDto>> Search(
        [FromQuery] string? itemId,
        [FromQuery] string? languages,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = itemId is not null
                ? BuildRequestFromItem(itemId, languages)
                : new SubtitleSearchRequest
                  {
                      Languages = ParseLanguages(languages),
                  };

            var results = await _aggregator
                .SearchAsync(request, cancellationToken)
                .ConfigureAwait(false);

            return Ok(new SearchResultDto
            {
                Results = results
                    .Select(r => SubtitleResultDto.From(r, SourceName(r.SourceId)))
                    .ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed for item {ItemId}", itemId);
            return Ok(new SearchResultDto { Error = ex.Message });
        }
    }

    /// <summary>
    /// Manual search: free-text query across all enabled sources.
    /// </summary>
    [HttpGet("search/manual")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<SearchResultDto>> ManualSearch(
        [FromQuery] string query,
        [FromQuery] string? languages,
        [FromQuery] int? season,
        [FromQuery] int? episode,
        CancellationToken cancellationToken)
    {
        var request = new SubtitleSearchRequest
        {
            Title         = query,
            SeriesTitle   = query,
            Languages     = ParseLanguages(languages),
            SeasonNumber  = season,
            EpisodeNumber = episode,
        };

        var results = await _aggregator.SearchAsync(request, cancellationToken).ConfigureAwait(false);

        return Ok(new SearchResultDto
        {
            Results = results
                .Select(r => SubtitleResultDto.From(r, SourceName(r.SourceId)))
                .ToList()
        });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // DOWNLOAD
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Download a selected subtitle and save it next to the media file.
    /// </summary>
    [HttpPost("download")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<DownloadResponseDto>> Download(
        [FromBody] DownloadRequestDto dto,
        CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById(Guid.Parse(dto.ItemId));
        if (item is null || string.IsNullOrEmpty(item.Path))
        {
            return Ok(new DownloadResponseDto { Success = false, Error = "Item not found" });
        }

        var result = new SubtitleResult
        {
            Id           = dto.SubtitleId,
            SourceId     = dto.SourceId,
            ReleaseName  = dto.ReleaseName,
            Language     = dto.Language,
            DownloadUrl  = dto.DownloadUrl,
            Uploader     = dto.Uploader,
            ReleaseGroup = dto.ReleaseGroup,
        };

        var download = await _fileService
            .DownloadAndSaveAsync(result, item.Path, cancellationToken)
            .ConfigureAwait(false);

        // Optional auto-sync
        var cfg = Plugin.Instance!.Configuration;
        if (download.Success && cfg.AutoSyncAfterDownload != "Off")
        {
            await AutoSync(download.SavedPath, item.Path, cfg.AutoSyncAfterDownload, cancellationToken)
                .ConfigureAwait(false);
        }

        return Ok(new DownloadResponseDto
        {
            Success   = download.Success,
            SavedPath = download.SavedPath,
            Error     = download.Error,
        });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SERIES / FOLDER DOWNLOAD
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Analyse a series (or season) — return per-episode subtitle coverage.
    /// </summary>
    [HttpPost("series/analyze")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<SeriesAnalysisDto> SeriesAnalyze([FromBody] SeriesAnalyzeRequestDto dto)
    {
        var item = _libraryManager.GetItemById(Guid.Parse(dto.ItemId));
        if (item is null)
        {
            return NotFound();
        }

        var episodes = GetMediaItems(item);
        var language = string.IsNullOrWhiteSpace(dto.Language)
            ? Plugin.Instance!.Configuration.PreferredLanguages.FirstOrDefault() ?? "en"
            : dto.Language;

        var entries = episodes.Select(ep => new EpisodeEntryDto
        {
            ItemId        = ep.Id.ToString(),
            Label         = BuildMediaLabel(ep),
            SeasonNumber  = ep is Episode episode ? episode.ParentIndexNumber ?? 0 : 0,
            EpisodeNumber = ep is Episode episode2 ? episode2.IndexNumber ?? 0 : 0,
            HasSubtitle   = SubtitleFileService.SubtitleExists(ep.Path!, language),
        }).ToList();

        return Ok(new SeriesAnalysisDto
        {
            SeriesTitle = item.Name,
            Episodes    = entries,
        });
    }

    /// <summary>
    /// Given the user's episode-1 subtitle choice, find matching candidates for
    /// all remaining episodes using uploader-name / release-group matching.
    /// </summary>
    [HttpPost("series/match")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<SeriesAnalysisDto>> SeriesMatch(
        [FromBody] SeriesMatchRequestDto dto,
        CancellationToken cancellationToken)
    {
        var seriesItem = _libraryManager.GetItemById(Guid.Parse(dto.SeriesItemId));
        if (seriesItem is null) return NotFound();

        var language = dto.Language;
        var episodes = GetMediaItems(seriesItem);

        var entries = episodes.Select((ep, idx) => new EpisodeSubtitleEntry
        {
            ItemId        = ep.Id.ToString(),
            Label         = BuildMediaLabel(ep),
            MediaPath     = ep.Path!,
            SearchTitle   = ep.Name,
            SeriesTitle   = ep is Episode episode ? episode.SeriesName : null,
            SeasonNumber  = ep is Episode episode2 ? episode2.ParentIndexNumber ?? 0 : 0,
            EpisodeNumber = ep is Episode episode3 ? episode3.IndexNumber ?? 0 : 0,
            AlreadyHasSubtitle = SubtitleFileService.SubtitleExists(ep.Path!, language),
            ChosenSubtitle = ep.Id.ToString().Equals(dto.AnchorItemId, StringComparison.OrdinalIgnoreCase)
                ? DtoToResult(dto.Anchor)
                : null,
            MatchMethod    = ep.Id.ToString().Equals(dto.AnchorItemId, StringComparison.OrdinalIgnoreCase)
                ? "Manual"
                : string.Empty,
        }).ToList();

        var anchor = DtoToResult(dto.Anchor);
        await _seriesMatcher
            .MatchEpisodesAsync(entries, anchor, language, cancellationToken)
            .ConfigureAwait(false);

        return Ok(new SeriesAnalysisDto
        {
            SeriesTitle = seriesItem.Name,
            Episodes    = entries.Select(e => new EpisodeEntryDto
            {
                ItemId        = e.ItemId,
                Label         = e.Label,
                SeasonNumber  = e.SeasonNumber,
                EpisodeNumber = e.EpisodeNumber,
                HasSubtitle   = e.AlreadyHasSubtitle,
                MatchMethod   = e.MatchMethod,
                ChosenSubtitle = e.ChosenSubtitle is null
                    ? null
                    : SubtitleResultDto.From(e.ChosenSubtitle, SourceName(e.ChosenSubtitle.SourceId)),
            }).ToList(),
        });
    }

    /// <summary>
    /// Batch-download subtitles for all supplied episodes.
    /// </summary>
    [HttpPost("series/batch-download")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<BatchDownloadResultDto>> SeriesBatchDownload(
        [FromBody] SeriesBatchDownloadRequestDto dto,
        CancellationToken cancellationToken)
    {
        var results = new List<BatchItemResultDto>();

        foreach (var item in dto.Items)
        {
            var subtitleResult = new SubtitleResult
            {
                Id           = item.SubtitleId,
                SourceId     = item.SourceId,
                ReleaseName  = item.ReleaseName,
                Language     = item.Language,
                DownloadUrl  = item.DownloadUrl,
                Uploader     = item.Uploader,
                ReleaseGroup = item.ReleaseGroup,
            };

            var download = await _fileService
                .DownloadAndSaveAsync(subtitleResult, item.MediaPath, cancellationToken)
                .ConfigureAwait(false);

            results.Add(new BatchItemResultDto
            {
                ItemId    = item.ItemId,
                Label     = item.Label ?? string.Empty,
                Success   = download.Success,
                SavedPath = download.SavedPath,
                Error     = download.Error,
            });
        }

        return Ok(new BatchDownloadResultDto { Results = results });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // PLAYER TEST
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Download a subtitle to temp and return the VLC command + XSPF playlist
    /// so the user can test sync in their local player.
    /// </summary>
    [HttpPost("player/test")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> PlayerTest(
        [FromBody] DownloadRequestDto dto,
        CancellationToken cancellationToken)
    {
        var item = _libraryManager.GetItemById(Guid.Parse(dto.ItemId));
        if (item is null || string.IsNullOrEmpty(item.Path))
        {
            return BadRequest("Item not found");
        }

        // Download to a temporary file so the original is not yet committed
        var tempDir  = Path.GetTempPath();
        var tempSrt  = Path.Combine(tempDir,
            $"jellysub_test_{dto.ItemId}_{dto.Language}.srt");

        var result = new SubtitleResult
        {
            Id          = dto.SubtitleId,
            SourceId    = dto.SourceId,
            ReleaseName = dto.ReleaseName,
            Language    = dto.Language,
            DownloadUrl = dto.DownloadUrl,
        };

        var source = _sources.FirstOrDefault(s => s.Id == dto.SourceId);
        if (source is null) return BadRequest("Unknown source");

        var content = await source.DownloadAsync(result, cancellationToken).ConfigureAwait(false);
        if (content is null) return BadRequest("Download failed");

        await System.IO.File.WriteAllTextAsync(tempSrt, content, cancellationToken)
            .ConfigureAwait(false);

        var videoPath = item.Path;
        var vlcCommand = $"vlc \"{videoPath}\" --sub-file=\"{tempSrt}\"";
        var xspf       = BuildXspf(videoPath, tempSrt);

        return Ok(new
        {
            VideoPath     = videoPath,
            SubtitlePath  = tempSrt,
            VlcCommand    = vlcCommand,
            XspfContent   = xspf,
        });
    }

    /// <summary>
    /// Download the XSPF playlist for the player test directly as a file.
    /// </summary>
    [HttpGet("player/playlist")]
    [Produces("application/xspf+xml")]
    public IActionResult PlayerPlaylist(
        [FromQuery] string videoPath,
        [FromQuery] string subtitlePath)
    {
        var xspf = BuildXspf(videoPath, subtitlePath);
        return File(
            System.Text.Encoding.UTF8.GetBytes(xspf),
            "application/xspf+xml",
            "jellysub-test.xspf");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // SYNC
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Return the status (installed / version) of all sync tools.</summary>
    [HttpGet("sync/tools")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<SyncToolsStatusDto> GetSyncTools()
        => Ok(new SyncToolsStatusDto { Tools = _syncManager.GetAllStatuses() });

    /// <summary>Install a sync tool (ffsubsync via pip, alass via GitHub releases).</summary>
    [HttpPost("sync/tools/install")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<InstallToolResponseDto>> InstallTool(
        [FromBody] InstallToolRequestDto dto,
        CancellationToken cancellationToken)
    {
        var (success, output) = dto.ToolId.ToLowerInvariant() switch
        {
            "ffsubsync" => await _syncManager
                .InstallFfsubsyncAsync(cancellationToken).ConfigureAwait(false),
            "alass"     => await _syncManager
                .InstallAlassAsync(cancellationToken).ConfigureAwait(false),
            _           => (false, $"Unknown tool '{dto.ToolId}'"),
        };

        return Ok(new InstallToolResponseDto { Success = success, Output = output });
    }

    /// <summary>Run a sync tool on a subtitle file.</summary>
    [HttpPost("sync")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<SyncResponseDto>> Sync(
        [FromBody] SyncRequestDto dto,
        CancellationToken cancellationToken)
    {
        var request = new Jellyfin.Plugin.JellySub.Sync.SyncRequest
        {
            VideoPath             = dto.VideoPath,
            SubtitlePath          = dto.SubtitlePath,
            ReferenceSubtitlePath = dto.ReferenceSubtitlePath,
            OutputPath            = dto.OutputPath,
        };

        var result = await _syncManager
            .SyncAsync(dto.ToolId, request, cancellationToken)
            .ConfigureAwait(false);

        return Ok(new SyncResponseDto
        {
            Success    = result.Success,
            OutputPath = result.OutputPath,
            ToolOutput = result.ToolOutput,
            Error      = result.Error,
        });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // LIBRARY SCAN
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Trigger a library scan (background task).</summary>
    [HttpPost("scan/trigger")]
    [Authorize(Policy = "RequiresElevation")]
    public IActionResult TriggerScan()
    {
        _taskManager.Execute<LibraryScanTask>();
        return Accepted();
    }

    /// <summary>Return current scan running state + log of last run.</summary>
    [HttpGet("scan/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<ScanStatusDto> ScanStatus()
    {
        return Ok(new ScanStatusDto
        {
            IsRunning = LibraryScanTask.IsRunning,
            Log       = LibraryScanTask.GetLastScanLog().Select(ScanLogEntryDto.From).ToList(),
        });
    }

    // ═════════════════════════════════════════════════════════════════════════
    // CONFIGURATION
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>Return the current plugin configuration (for the settings page).</summary>
    [HttpGet("config")]
    public IActionResult GetConfig()
        => Ok(Plugin.Instance!.Configuration);

    /// <summary>Save plugin configuration.</summary>
    [HttpPost("config")]
    [Authorize(Policy = "RequiresElevation")]
    public IActionResult SaveConfig([FromBody] Configuration.PluginConfiguration cfg)
    {
        Plugin.Instance!.UpdateConfiguration(cfg);
        return NoContent();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Private helpers
    // ═════════════════════════════════════════════════════════════════════════

    private SubtitleSearchRequest BuildRequestFromItem(string itemId, string? languagesParam)
    {
        var item = _libraryManager.GetItemById(Guid.Parse(itemId));
        if (item is null) throw new InvalidOperationException($"Item {itemId} not found");

        var req = new SubtitleSearchRequest
        {
            ItemId        = itemId,
            Title         = item.Name,
            Year          = item.ProductionYear,
            MediaFilePath = item.Path,
            Languages     = languagesParam is not null
                ? ParseLanguages(languagesParam)
                : Plugin.Instance!.Configuration.PreferredLanguages.ToList(),
        };

        // IMDb
        var imdb = item.GetProviderId(MediaBrowser.Model.Entities.MetadataProvider.Imdb);
        if (!string.IsNullOrEmpty(imdb)) req.ImdbId = imdb;

        if (item is Episode ep)
        {
            req.SeriesTitle   = ep.SeriesName;
            req.Title         = ep.SeriesName;
            req.SeasonNumber  = ep.ParentIndexNumber;
            req.EpisodeNumber = ep.IndexNumber;
        }

        return req;
    }

    private List<BaseItem> GetMediaItems(BaseItem rootItem)
    {
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = new[] { BaseItemKind.Movie, BaseItemKind.Episode, BaseItemKind.Video },
            IsVirtualItem    = false,
            Recursive        = true,
            AncestorIds      = new[] { rootItem.Id },
        };

        var items = _libraryManager
            .GetItemList(query)
            .Where(i => !string.IsNullOrEmpty(i.Path))
            .ToList();

        if (!string.IsNullOrEmpty(rootItem.Path) &&
            rootItem is not Folder &&
            items.All(i => i.Id != rootItem.Id))
        {
            items.Add(rootItem);
        }

        return items
            .OrderBy(i => i is Episode ep ? 0 : 1)
            .ThenBy(i => i is Episode ep ? ep.ParentIndexNumber ?? 0 : 0)
            .ThenBy(i => i is Episode ep ? ep.IndexNumber ?? 0 : 0)
            .ThenBy(i => i.Path)
            .ThenBy(i => i.Name)
            .ToList();
    }

    private static string BuildMediaLabel(BaseItem item)
    {
        if (item is Episode ep)
        {
            return $"S{ep.ParentIndexNumber:00}E{ep.IndexNumber:00} – {ep.Name}";
        }

        return item.Name;
    }

    private static List<string> ParseLanguages(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private string SourceName(string sourceId)
        => _sources.FirstOrDefault(s => s.Id == sourceId)?.DisplayName ?? sourceId;

    private static SubtitleResult DtoToResult(SubtitleResultDto dto) => new()
    {
        Id           = dto.Id,
        SourceId     = dto.SourceId,
        ReleaseName  = dto.ReleaseName,
        Language     = dto.Language,
        LanguageName = dto.LanguageName,
        DownloadCount = dto.DownloadCount,
        Uploader     = dto.Uploader,
        UploadDate   = dto.UploadDate,
        IsHashMatch  = dto.IsHashMatch,
        IsHearingImpaired = dto.IsHearingImpaired,
        ReleaseGroup = dto.ReleaseGroup,
        DownloadUrl  = dto.DownloadUrl,
    };

    private static string BuildXspf(string videoPath, string subtitlePath)
    {
        var videoUri = new Uri(videoPath).AbsoluteUri;
        var subUri   = new Uri(subtitlePath).AbsoluteUri;
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <playlist version="1" xmlns="http://xspf.org/ns/0/"
                      xmlns:vlc="http://www.videolan.org/vlc/playlist/ns/0/">
              <trackList>
                <track>
                  <location>{videoUri}</location>
                  <extension application="http://www.videolan.org/vlc/playlist/0">
                    <vlc:option>sub-file={subUri}</vlc:option>
                  </extension>
                </track>
              </trackList>
            </playlist>
            """;
    }

    private async Task AutoSync(
        string subtitlePath,
        string videoPath,
        string toolId,
        CancellationToken ct)
    {
        try
        {
            var request = new Jellyfin.Plugin.JellySub.Sync.SyncRequest
            {
                VideoPath    = videoPath,
                SubtitlePath = subtitlePath,
                OutputPath   = subtitlePath, // overwrite in place
            };
            await _syncManager.SyncAsync(toolId, request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Auto-sync failed for {Path}", subtitlePath);
        }
    }
}
