using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
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

    /// <summary>Initializes the JellySub API controller.</summary>
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
            BaseItem? item = null;
            if (!string.IsNullOrWhiteSpace(itemId))
            {
                item = _libraryManager.GetItemById(Guid.Parse(itemId));
            }

            var request = !string.IsNullOrWhiteSpace(itemId)
                ? BuildRequestFromItem(itemId, languages)
                : new SubtitleSearchRequest
                  {
                      Languages = ParseLanguages(languages),
                  };

            var results = await _aggregator
                .SearchAsync(request, cancellationToken)
                .ConfigureAwait(false);

            string? searchTitle = null;
            int? searchYear = null;
            if (item is not null)
            {
                (searchTitle, searchYear) = BuildSearchLabel(item);
            }

            return Ok(new SearchResultDto
            {
                SearchTitle = searchTitle,
                SearchYear  = searchYear,
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
            SearchTitle = query,
            SearchYear  = null,
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

    /// <summary>Returns detected Jellyfin web roots and their patch status.</summary>
    [HttpGet("webclient/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult WebClientStatus()
    {
        var candidates = GetDefaultWebRoots().ToList();
        var patched = candidates.Where(IsWebClientPatched).ToList();
        return Ok(new
        {
            Platform = CurrentPlatform(),
            CandidateRoots = candidates,
            PatchedRoots = patched,
        });
    }

    /// <summary>Downloads an installer or uninstaller script for patching the Jellyfin web client.</summary>
    [HttpGet("webclient/script")]
    [Authorize(Policy = "RequiresElevation")]
    public IActionResult DownloadWebClientScript([FromQuery] string platform, [FromQuery] string? mode)
    {
        var normalized = (platform ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedMode = string.IsNullOrWhiteSpace(mode) ? "install" : mode.Trim().ToLowerInvariant();
        var uninstall = normalizedMode == "uninstall" || normalizedMode == "remove" || normalizedMode == "revert";

        var content = (normalized, uninstall) switch
        {
            ("linux", false) => BuildLinuxInstallerScript(),
            ("linux", true) => BuildLinuxUninstallScript(),
            ("mac", false) or ("macos", false) or ("osx", false) => BuildMacInstallerScript(),
            ("mac", true) or ("macos", true) or ("osx", true) => BuildMacUninstallScript(),
            ("win", false) or ("windows", false) => BuildWindowsInstallerScript(),
            ("win", true) or ("windows", true) => BuildWindowsUninstallScript(),
            _ => throw new ArgumentException("Unknown platform. Use linux, macos, or windows."),
        };

        var fileName = (normalized, uninstall) switch
        {
            ("linux", false) => "install-jellysub-web-client-linux.sh",
            ("linux", true) => "uninstall-jellysub-web-client-linux.sh",
            ("mac", false) or ("macos", false) or ("osx", false) => "install-jellysub-web-client-macos.sh",
            ("mac", true) or ("macos", true) or ("osx", true) => "uninstall-jellysub-web-client-macos.sh",
            (_, false) => "install-jellysub-web-client-windows.ps1",
            _ => "uninstall-jellysub-web-client-windows.ps1",
        };

        return File(Encoding.UTF8.GetBytes(content), "application/octet-stream", fileName);
    }

    /// <summary>Patches default Jellyfin web roots on the current machine with the JellySub web client script.</summary>
    [HttpPost("webclient/install-defaults")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult InstallWebClientDefaults()
    {
        var results = PatchDefaultWebRoots(install: true);
        var patchedCount = results.Count(r => r.Status == "Patched");
        return Ok(new
        {
            Success = patchedCount > 0,
            Message = patchedCount > 0
                ? $"Patched {patchedCount} default Jellyfin web root(s). Restart Jellyfin / Jellyfin Desktop and clear cache."
                : "No default Jellyfin web root could be patched automatically. Download the platform script and run it manually on the machine hosting the Jellyfin web files.",
            Results = results,
        });
    }

    /// <summary>Removes the JellySub web client script from default Jellyfin web roots on the current machine.</summary>
    [HttpPost("webclient/uninstall-defaults")]
    [Authorize(Policy = "RequiresElevation")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult UninstallWebClientDefaults()
    {
        var results = PatchDefaultWebRoots(install: false);
        var revertedCount = results.Count(r => r.Status == "Reverted");
        return Ok(new
        {
            Success = revertedCount > 0,
            Message = revertedCount > 0
                ? $"Reverted {revertedCount} default Jellyfin web root(s). Restart Jellyfin / Jellyfin Desktop and clear cache."
                : "No default Jellyfin web root could be reverted automatically. Download the platform uninstall script and run it manually on the machine hosting the Jellyfin web files.",
            Results = results,
        });
    }

    /// <summary>Save plugin configuration.</summary>
    [HttpPost("config")]
    [Authorize(Policy = "RequiresElevation")]
    public IActionResult SaveConfig([FromBody] Configuration.PluginConfiguration cfg)
    {
        Plugin.Instance!.UpdateConfiguration(cfg);
        return Ok(Plugin.Instance.Configuration);
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

        if (string.IsNullOrEmpty(imdb) && item is Episode ep)
        {
            var parent = ep.GetParent();
            imdb = parent?.GetProviderId(MediaBrowser.Model.Entities.MetadataProvider.Imdb);
        }

        if (!string.IsNullOrEmpty(imdb)) req.ImdbId = imdb;

        if (item is Episode episode)
        {
            req.SeriesTitle   = episode.SeriesName;
            req.Title         = episode.SeriesName;
            req.SeasonNumber  = episode.ParentIndexNumber;
            req.EpisodeNumber = episode.IndexNumber;
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

    private static (string Title, int? Year) BuildSearchLabel(BaseItem item)
    {
        if (item is Episode ep && !string.IsNullOrWhiteSpace(ep.SeriesName))
        {
            return (ep.SeriesName, item.ProductionYear);
        }

        return (item.Name, item.ProductionYear);
    }

    private static List<string> ParseLanguages(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static string CurrentPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "Windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "macOS";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "Linux";
        return RuntimeInformation.OSDescription;
    }

    private static IEnumerable<string> GetDefaultWebRoots()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            return new[]
            {
                @"C:\Program Files\Jellyfin\Server\jellyfin-web",
                @"C:\Program Files\Jellyfin\jellyfin-web",
                @"C:\Program Files\Jellyfin\Tray\resources\jellyfin-web",
                @"C:\Program Files\Jellyfin\Tray\jellyfin-web",
                @"C:\Program Files\Jellyfin\Media Player\resources\jellyfin-web",
                @"C:\Program Files\Jellyfin\Media Player\jellyfin-web",
                Path.Combine(programFiles, "Jellyfin", "Server", "jellyfin-web"),
                Path.Combine(programFiles, "Jellyfin", "jellyfin-web"),
                Path.Combine(programFiles, "Jellyfin", "Tray", "resources", "jellyfin-web"),
                Path.Combine(programFiles, "Jellyfin", "Tray", "jellyfin-web"),
                Path.Combine(programFiles, "Jellyfin", "Media Player", "resources", "jellyfin-web"),
                Path.Combine(programFiles, "Jellyfin", "Media Player", "jellyfin-web"),
                Path.Combine(localAppData, @"Programs\Jellyfin\resources\jellyfin-web"),
                Path.Combine(localAppData, @"Programs\Jellyfin Desktop\resources\jellyfin-web"),
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new[]
            {
                "/Applications/Jellyfin.app/Contents/Resources/jellyfin-web",
                "/Applications/Jellyfin Desktop.app/Contents/Resources/jellyfin-web",
                Path.Combine(home, "Applications/Jellyfin.app/Contents/Resources/jellyfin-web"),
                Path.Combine(home, "Applications/Jellyfin Desktop.app/Contents/Resources/jellyfin-web"),
            };
        }

        return new[]
        {
            "/usr/share/jellyfin/web",
            "/var/lib/jellyfin/web",
            "/opt/jellyfin-web",
        };
    }

    private static bool IsWebClientPatched(string root)
    {
        var pluginPath = Path.Combine(root, "jellysub-context-plugin.js");
        var indexPath = Path.Combine(root, "index.html");
        var configPath = Path.Combine(root, "config.json");
        if (!System.IO.File.Exists(pluginPath) || !System.IO.File.Exists(indexPath) || !System.IO.File.Exists(configPath))
        {
            return false;
        }

        var indexText = System.IO.File.ReadAllText(indexPath);
        var configText = System.IO.File.ReadAllText(configPath);
        return indexText.Contains("jellysub-context-plugin.js", StringComparison.OrdinalIgnoreCase)
            && configText.Contains("jellysubContext", StringComparison.OrdinalIgnoreCase);
    }

    private List<WebClientInstallResult> PatchDefaultWebRoots(bool install)
    {
        var results = new List<WebClientInstallResult>();
        var pluginScript = install ? GetEmbeddedText("WebClient.jellysub-context-plugin.js") : null;

        foreach (var root in GetDefaultWebRoots())
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    results.Add(new WebClientInstallResult { Path = root, Status = "Skipped", Message = "Path not found" });
                    continue;
                }

                var indexPath = Path.Combine(root, "index.html");
                var configPath = Path.Combine(root, "config.json");
                if (!System.IO.File.Exists(indexPath) || !System.IO.File.Exists(configPath))
                {
                    results.Add(new WebClientInstallResult { Path = root, Status = "Skipped", Message = "Missing index.html or config.json" });
                    continue;
                }

                if (install)
                {
                    System.IO.File.WriteAllText(Path.Combine(root, "jellysub-context-plugin.js"), pluginScript!);
                    PatchIndexHtml(indexPath);
                    PatchConfigJson(configPath);
                    results.Add(new WebClientInstallResult { Path = root, Status = "Patched", Message = "Installed JellySub web-client plugin" });
                }
                else
                {
                    RemoveWebClientPatch(root, indexPath, configPath);
                    results.Add(new WebClientInstallResult { Path = root, Status = "Reverted", Message = "Removed JellySub web-client plugin" });
                }
            }
            catch (Exception ex)
            {
                results.Add(new WebClientInstallResult { Path = root, Status = "Failed", Message = ex.Message });
            }
        }

        return results;
    }

    private string GetEmbeddedText(string suffix)
    {
        var asm = typeof(Plugin).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.Ordinal));
        if (resourceName is null)
        {
            throw new FileNotFoundException($"Embedded resource not found: {suffix}");
        }

        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Could not open resource: {resourceName}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void PatchIndexHtml(string indexPath)
    {
        var text = System.IO.File.ReadAllText(indexPath);
        if (text.Contains("jellysub-context-plugin.js", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        const string tag = "    <script src=\"jellysub-context-plugin.js\"></script>\n";
        text = text.Contains("</body>", StringComparison.OrdinalIgnoreCase)
            ? text.Replace("</body>", tag + "</body>", StringComparison.OrdinalIgnoreCase)
            : text.Replace("</head>", tag + "</head>", StringComparison.OrdinalIgnoreCase);

        System.IO.File.WriteAllText(indexPath, text);
    }

    private static void PatchConfigJson(string configPath)
    {
        var json = JsonNode.Parse(System.IO.File.ReadAllText(configPath))?.AsObject()
            ?? new JsonObject();
        var plugins = json["plugins"] as JsonArray ?? new JsonArray();
        if (json["plugins"] is null)
        {
            json["plugins"] = plugins;
        }

        if (!plugins.Any(p => string.Equals(p?.GetValue<string>(), "jellysubContext", StringComparison.OrdinalIgnoreCase)))
        {
            plugins.Add("jellysubContext");
        }

        System.IO.File.WriteAllText(configPath, json.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static void RemoveWebClientPatch(string root, string indexPath, string configPath)
    {
        var pluginPath = Path.Combine(root, "jellysub-context-plugin.js");
        if (System.IO.File.Exists(pluginPath))
        {
            System.IO.File.Delete(pluginPath);
        }

        var indexText = System.IO.File.ReadAllText(indexPath)
            .Replace("    <script src=\"jellysub-context-plugin.js\"></script>\r\n", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("    <script src=\"jellysub-context-plugin.js\"></script>\n", string.Empty, StringComparison.OrdinalIgnoreCase);
        System.IO.File.WriteAllText(indexPath, indexText);

        var json = JsonNode.Parse(System.IO.File.ReadAllText(configPath))?.AsObject();
        var plugins = json?["plugins"] as JsonArray;
        if (plugins is not null)
        {
            for (var i = plugins.Count - 1; i >= 0; i--)
            {
                if (string.Equals(plugins[i]?.GetValue<string>(), "jellysubContext", StringComparison.OrdinalIgnoreCase))
                {
                    plugins.RemoveAt(i);
                }
            }

            System.IO.File.WriteAllText(configPath, json!.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static string BuildLinuxInstallerScript() => "#!/usr/bin/env bash\n" +
        "set -euo pipefail\n\n" +
        "PLUGIN_URL=\"https://raw.githubusercontent.com/McLytir/JellySub/main/web-client/jellysub-context-plugin.js\"\n" +
        "CANDIDATES=(\n" +
        "  \"/usr/share/jellyfin/web\"\n" +
        "  \"/var/lib/jellyfin/web\"\n" +
        "  \"/opt/jellyfin-web\"\n" +
        ")\n\n" +
        "patch_root() {\n" +
        "  local root=\"$1\"\n" +
        "  local plugin=\"$root/jellysub-context-plugin.js\"\n" +
        "  local index=\"$root/index.html\"\n" +
        "  local config=\"$root/config.json\"\n" +
        "  [[ -f \"$index\" && -f \"$config\" ]] || return 1\n" +
        "  curl -fsSL \"$PLUGIN_URL\" -o \"$plugin\"\n" +
        "  grep -q 'jellysub-context-plugin.js' \"$index\" || python3 - <<PY\n" +
        "from pathlib import Path\n" +
        "p = Path(r'''$index''')\n" +
        "text = p.read_text(encoding='utf-8')\n" +
        "text = text.replace('</body>', '    <script src=\"jellysub-context-plugin.js\"></script>\\n</body>', 1) if '</body>' in text else text.replace('</head>', '    <script src=\"jellysub-context-plugin.js\"></script>\\n</head>', 1)\n" +
        "p.write_text(text, encoding='utf-8')\n" +
        "PY\n" +
        "  python3 - <<PY\n" +
        "import json\nfrom pathlib import Path\n" +
        "p = Path(r'''$config''')\n" +
        "data = json.loads(p.read_text(encoding='utf-8'))\n" +
        "plugins = data.setdefault('plugins', [])\n" +
        "if 'jellysubContext' not in plugins: plugins.append('jellysubContext')\n" +
        "p.write_text(json.dumps(data, indent=2) + '\\n', encoding='utf-8')\n" +
        "PY\n" +
        "  echo \"Patched: $root\"\n" +
        "}\n\n" +
        "found=0\nfor root in \"${CANDIDATES[@]}\"; do\n  if patch_root \"$root\"; then found=1; fi\ndone\n" +
        "if [[ \"$found\" -eq 0 ]]; then echo \"No default Jellyfin web root found. Edit CANDIDATES in this script for a custom install.\"; exit 1; fi\n" +
        "echo \"Done. Restart Jellyfin / clear browser cache.\"\n";

    private static string BuildMacInstallerScript() => "#!/usr/bin/env bash\n" +
        "set -euo pipefail\n\n" +
        "PLUGIN_URL=\"https://raw.githubusercontent.com/McLytir/JellySub/main/web-client/jellysub-context-plugin.js\"\n" +
        "CANDIDATES=(\n" +
        "  \"/Applications/Jellyfin.app/Contents/Resources/jellyfin-web\"\n" +
        "  \"/Applications/Jellyfin Desktop.app/Contents/Resources/jellyfin-web\"\n" +
        "  \"$HOME/Applications/Jellyfin.app/Contents/Resources/jellyfin-web\"\n" +
        "  \"$HOME/Applications/Jellyfin Desktop.app/Contents/Resources/jellyfin-web\"\n" +
        ")\n\n" +
        "patch_root() {\n" +
        "  local root=\"$1\"\n" +
        "  local plugin=\"$root/jellysub-context-plugin.js\"\n" +
        "  local index=\"$root/index.html\"\n" +
        "  local config=\"$root/config.json\"\n" +
        "  [[ -f \"$index\" && -f \"$config\" ]] || return 1\n" +
        "  curl -fsSL \"$PLUGIN_URL\" -o \"$plugin\"\n" +
        "  grep -q 'jellysub-context-plugin.js' \"$index\" || python3 - <<PY\n" +
        "from pathlib import Path\n" +
        "p = Path(r'''$index''')\n" +
        "text = p.read_text(encoding='utf-8')\n" +
        "text = text.replace('</body>', '    <script src=\"jellysub-context-plugin.js\"></script>\\n</body>', 1) if '</body>' in text else text.replace('</head>', '    <script src=\"jellysub-context-plugin.js\"></script>\\n</head>', 1)\n" +
        "p.write_text(text, encoding='utf-8')\n" +
        "PY\n" +
        "  python3 - <<PY\n" +
        "import json\nfrom pathlib import Path\n" +
        "p = Path(r'''$config''')\n" +
        "data = json.loads(p.read_text(encoding='utf-8'))\n" +
        "plugins = data.setdefault('plugins', [])\n" +
        "if 'jellysubContext' not in plugins: plugins.append('jellysubContext')\n" +
        "p.write_text(json.dumps(data, indent=2) + '\\n', encoding='utf-8')\n" +
        "PY\n" +
        "  echo \"Patched: $root\"\n" +
        "}\n\n" +
        "found=0\nfor root in \"${CANDIDATES[@]}\"; do\n  if patch_root \"$root\"; then found=1; fi\ndone\n" +
        "if [[ \"$found\" -eq 0 ]]; then echo \"No default Jellyfin web root found. Edit CANDIDATES in this script for a custom install.\"; exit 1; fi\n" +
        "echo \"Done. Restart Jellyfin / Jellyfin Desktop and clear browser cache.\"\n";

    private static string BuildWindowsInstallerScript() =>
        "$ErrorActionPreference = 'Stop'\r\n\r\n" +
        "$pluginUrl = 'https://raw.githubusercontent.com/McLytir/JellySub/main/web-client/jellysub-context-plugin.js'\r\n" +
        "$candidates = @(\r\n" +
        "  'C:\\Program Files\\Jellyfin\\Server\\jellyfin-web',\r\n" +
        "  'C:\\Program Files\\Jellyfin\\jellyfin-web',\r\n" +
        "  'C:\\Program Files\\Jellyfin\\Tray\\resources\\jellyfin-web',\r\n" +
        "  'C:\\Program Files\\Jellyfin\\Tray\\jellyfin-web',\r\n" +
        "  'C:\\Program Files\\Jellyfin\\Media Player\\resources\\jellyfin-web',\r\n" +
        "  'C:\\Program Files\\Jellyfin\\Media Player\\jellyfin-web',\r\n" +
        "  \"$env:LOCALAPPDATA\\Programs\\Jellyfin\\resources\\jellyfin-web\",\r\n" +
        "  \"$env:LOCALAPPDATA\\Programs\\Jellyfin Desktop\\resources\\jellyfin-web\"\r\n" +
        ")\r\n\r\n" +
        "function Patch-Root([string]$root) {\r\n" +
        "  $index = Join-Path $root 'index.html'\r\n" +
        "  $config = Join-Path $root 'config.json'\r\n" +
        "  $plugin = Join-Path $root 'jellysub-context-plugin.js'\r\n" +
        "  if (!(Test-Path $index) -or !(Test-Path $config)) { return $false }\r\n" +
        "  Invoke-WebRequest -Uri $pluginUrl -OutFile $plugin\r\n" +
        "  $indexText = Get-Content $index -Raw\r\n" +
        "  if ($indexText -notmatch 'jellysub-context-plugin.js') {\r\n" +
        "    if ($indexText.Contains('</body>')) { $indexText = $indexText.Replace('</body>', \"    <script src=`\"jellysub-context-plugin.js`\"></script>`r`n</body>\") }\r\n" +
        "    else { $indexText = $indexText.Replace('</head>', \"    <script src=`\"jellysub-context-plugin.js`\"></script>`r`n</head>\") }\r\n" +
        "    Set-Content -Path $index -Value $indexText -Encoding UTF8\r\n" +
        "  }\r\n" +
        "  $json = Get-Content $config -Raw | ConvertFrom-Json\r\n" +
        "  if ($null -eq $json.plugins) { $json | Add-Member -NotePropertyName plugins -NotePropertyValue @() }\r\n" +
        "  if ($json.plugins -notcontains 'jellysubContext') { $json.plugins += 'jellysubContext'; $json | ConvertTo-Json -Depth 16 | Set-Content -Path $config -Encoding UTF8 }\r\n" +
        "  Write-Host \"Patched: $root\"\r\n" +
        "  return $true\r\n" +
        "}\r\n\r\n" +
        "$patched = $false\r\nforeach ($root in $candidates) { if (Patch-Root $root) { $patched = $true } }\r\n" +
        "if (-not $patched) { Write-Host 'No default Jellyfin web root found. Edit $candidates in this script for a custom install.'; exit 1 }\r\n" +
        "Write-Host 'Done. Restart Jellyfin / Jellyfin Desktop and clear browser cache.'\r\n";

    private static string BuildLinuxUninstallScript() => "#!/usr/bin/env bash\n" +
        "set -euo pipefail\n\n" +
        "CANDIDATES=(\n  \"/usr/share/jellyfin/web\"\n  \"/var/lib/jellyfin/web\"\n  \"/opt/jellyfin-web\"\n)\n\n" +
        "revert_root() {\n  local root=\"$1\"\n  local plugin=\"$root/jellysub-context-plugin.js\"\n  local index=\"$root/index.html\"\n  local config=\"$root/config.json\"\n  [[ -f \"$index\" && -f \"$config\" ]] || return 1\n  rm -f \"$plugin\"\n  python3 - <<PY\nfrom pathlib import Path\np = Path(r'''$index''')\ntext = p.read_text(encoding='utf-8').replace('    <script src=\"jellysub-context-plugin.js\"></script>\\n', '').replace('    <script src=\"jellysub-context-plugin.js\"></script>\\r\\n', '')\np.write_text(text, encoding='utf-8')\nPY\n  python3 - <<PY\nimport json\nfrom pathlib import Path\np = Path(r'''$config''')\ndata = json.loads(p.read_text(encoding='utf-8'))\nplugins = data.get('plugins', [])\nif isinstance(plugins, list): data['plugins'] = [p for p in plugins if p != 'jellysubContext']\np.write_text(json.dumps(data, indent=2) + '\\n', encoding='utf-8')\nPY\n  echo \"Reverted: $root\"\n}\n\n" +
        "found=0\nfor root in \"${CANDIDATES[@]}\"; do\n  if revert_root \"$root\"; then found=1; fi\ndone\nif [[ \"$found\" -eq 0 ]]; then echo \"No default Jellyfin web root found. Edit CANDIDATES in this script for a custom install.\"; exit 1; fi\necho \"Done. Restart Jellyfin / clear browser cache.\"\n";

    private static string BuildMacUninstallScript() => "#!/usr/bin/env bash\n" +
        "set -euo pipefail\n\n" +
        "CANDIDATES=(\n  \"/Applications/Jellyfin.app/Contents/Resources/jellyfin-web\"\n  \"/Applications/Jellyfin Desktop.app/Contents/Resources/jellyfin-web\"\n  \"$HOME/Applications/Jellyfin.app/Contents/Resources/jellyfin-web\"\n  \"$HOME/Applications/Jellyfin Desktop.app/Contents/Resources/jellyfin-web\"\n)\n\n" +
        "revert_root() {\n  local root=\"$1\"\n  local plugin=\"$root/jellysub-context-plugin.js\"\n  local index=\"$root/index.html\"\n  local config=\"$root/config.json\"\n  [[ -f \"$index\" && -f \"$config\" ]] || return 1\n  rm -f \"$plugin\"\n  python3 - <<PY\nfrom pathlib import Path\np = Path(r'''$index''')\ntext = p.read_text(encoding='utf-8').replace('    <script src=\"jellysub-context-plugin.js\"></script>\\n', '').replace('    <script src=\"jellysub-context-plugin.js\"></script>\\r\\n', '')\np.write_text(text, encoding='utf-8')\nPY\n  python3 - <<PY\nimport json\nfrom pathlib import Path\np = Path(r'''$config''')\ndata = json.loads(p.read_text(encoding='utf-8'))\nplugins = data.get('plugins', [])\nif isinstance(plugins, list): data['plugins'] = [p for p in plugins if p != 'jellysubContext']\np.write_text(json.dumps(data, indent=2) + '\\n', encoding='utf-8')\nPY\n  echo \"Reverted: $root\"\n}\n\n" +
        "found=0\nfor root in \"${CANDIDATES[@]}\"; do\n  if revert_root \"$root\"; then found=1; fi\ndone\nif [[ \"$found\" -eq 0 ]]; then echo \"No default Jellyfin web root found. Edit CANDIDATES in this script for a custom install.\"; exit 1; fi\necho \"Done. Restart Jellyfin / Jellyfin Desktop and clear browser cache.\"\n";

    private static string BuildWindowsUninstallScript() =>
        "$ErrorActionPreference = 'Stop'\r\n\r\n" +
        "$candidates = @(\r\n" +
        "  'C:\\Program Files\\Jellyfin\\Server\\jellyfin-web',\r\n" +
        "  'C:\\Program Files\\Jellyfin\\jellyfin-web',\r\n" +
        "  'C:\\Program Files\\Jellyfin\\Tray\\resources\\jellyfin-web',\r\n" +
        "  'C:\\Program Files\\Jellyfin\\Tray\\jellyfin-web',\r\n" +
        "  'C:\\Program Files\\Jellyfin\\Media Player\\resources\\jellyfin-web',\r\n" +
        "  'C:\\Program Files\\Jellyfin\\Media Player\\jellyfin-web',\r\n" +
        "  \"$env:LOCALAPPDATA\\Programs\\Jellyfin\\resources\\jellyfin-web\",\r\n" +
        "  \"$env:LOCALAPPDATA\\Programs\\Jellyfin Desktop\\resources\\jellyfin-web\"\r\n" +
        ")\r\n\r\n" +
        "function Revert-Root([string]$root) {\r\n" +
        "  $index = Join-Path $root 'index.html'\r\n  $config = Join-Path $root 'config.json'\r\n  $plugin = Join-Path $root 'jellysub-context-plugin.js'\r\n  if (!(Test-Path $index) -or !(Test-Path $config)) { return $false }\r\n  if (Test-Path $plugin) { Remove-Item $plugin -Force }\r\n  $indexText = Get-Content $index -Raw\r\n  $indexText = $indexText.Replace(\"    <script src=`\"jellysub-context-plugin.js`\"></script>`r`n\", '')\r\n  $indexText = $indexText.Replace(\"    <script src=`\"jellysub-context-plugin.js`\"></script>`n\", '')\r\n  Set-Content -Path $index -Value $indexText -Encoding UTF8\r\n  $json = Get-Content $config -Raw | ConvertFrom-Json\r\n  if ($null -ne $json.plugins) { $json.plugins = @($json.plugins | Where-Object { $_ -ne 'jellysubContext' }); $json | ConvertTo-Json -Depth 16 | Set-Content -Path $config -Encoding UTF8 }\r\n  Write-Host \"Reverted: $root\"\r\n  return $true\r\n}\r\n\r\n" +
        "$reverted = $false\r\nforeach ($root in $candidates) { if (Revert-Root $root) { $reverted = $true } }\r\nif (-not $reverted) { Write-Host 'No default Jellyfin web root found. Edit $candidates in this script for a custom install.'; exit 1 }\r\nWrite-Host 'Done. Restart Jellyfin / Jellyfin Desktop and clear browser cache.'\r\n";

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

    private sealed class WebClientInstallResult
    {
        public string Path { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Message { get; set; }
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
