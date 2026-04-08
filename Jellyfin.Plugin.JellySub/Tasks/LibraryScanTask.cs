using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellySub.Models;
using Jellyfin.Plugin.JellySub.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySub.Tasks;

/// <summary>
/// Scheduled task: scans the entire library, finds video files missing a subtitle
/// for any configured language, and auto-downloads the best available match.
/// </summary>
public sealed class LibraryScanTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly SubtitleAggregator _aggregator;
    private readonly SubtitleFileService _fileService;
    private readonly ITaskManager _taskManager;
    private readonly ILogger<LibraryScanTask> _logger;

    private readonly object _timerLock = new();
    private Timer? _afterRefreshTimer;
    private DateTime _lastLibraryChangeUtc = DateTime.MinValue;

    // Simple in-memory log of the last scan — read by the API controller
    private static readonly List<ScanLogEntry> _lastScanLog = new();
    private static readonly object _logLock = new();
    public static bool IsRunning { get; private set; }

    public static IReadOnlyList<ScanLogEntry> GetLastScanLog()
    {
        lock (_logLock)
        {
            return _lastScanLog.ToList().AsReadOnly();
        }
    }

    private static void ClearLog()
    {
        lock (_logLock)
        {
            _lastScanLog.Clear();
        }
    }

    private static void AddToLog(ScanLogEntry entry)
    {
        lock (_logLock)
        {
            _lastScanLog.Add(entry);
        }
    }

    public LibraryScanTask(
        ILibraryManager libraryManager,
        SubtitleAggregator aggregator,
        SubtitleFileService fileService,
        ITaskManager taskManager,
        ILogger<LibraryScanTask> logger)
    {
        _libraryManager = libraryManager;
        _aggregator     = aggregator;
        _fileService    = fileService;
        _taskManager    = taskManager;
        _logger         = logger;

        _libraryManager.ItemAdded += OnLibraryItemChanged;
        _libraryManager.ItemUpdated += OnLibraryItemChanged;
        _libraryManager.ItemRemoved += OnLibraryItemChanged;
    }

    public string Name        => "JellySub: Scan library for missing subtitles";
    public string Key         => "JellySubLibraryScan";
    public string Description => "Finds all videos without subtitles for the configured languages and auto-downloads the best available match.";
    public string Category    => "JellySub";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        var schedule = Plugin.Instance?.Configuration.BatchScanSchedule ?? "Manual";
        return schedule switch
        {
            "Daily" => new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerDaily,
                    TimeOfDayTicks = TimeSpan.FromHours(2).Ticks,
                },
            },
            "Weekly" => new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerWeekly,
                    DayOfWeek = DayOfWeek.Sunday,
                    TimeOfDayTicks = TimeSpan.FromHours(2).Ticks,
                },
            },
            _ => Array.Empty<TaskTriggerInfo>(),
        };
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            _logger.LogWarning("Library scan is already running — skipping");
            return;
        }

        IsRunning = true;
        ClearLog();

        try
        {
            await RunScanAsync(progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void OnLibraryItemChanged(object? sender, ItemChangeEventArgs e)
    {
        if (!ShouldAutoRunAfterLibraryRefresh())
        {
            return;
        }

        if (e.Item is null || e.Item.Path is null)
        {
            return;
        }

        lock (_timerLock)
        {
            _lastLibraryChangeUtc = DateTime.UtcNow;
            _afterRefreshTimer ??= new Timer(_ => TriggerDeferredScan(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _afterRefreshTimer.Change(TimeSpan.FromMinutes(5), Timeout.InfiniteTimeSpan);
        }
    }

    private bool ShouldAutoRunAfterLibraryRefresh()
        => string.Equals(Plugin.Instance?.Configuration.BatchScanSchedule, "AfterLibraryRefresh", StringComparison.OrdinalIgnoreCase);

    private void TriggerDeferredScan()
    {
        if (!ShouldAutoRunAfterLibraryRefresh() || IsRunning)
        {
            return;
        }

        lock (_timerLock)
        {
            if (DateTime.UtcNow - _lastLibraryChangeUtc < TimeSpan.FromMinutes(4))
            {
                _afterRefreshTimer?.Change(TimeSpan.FromMinutes(1), Timeout.InfiniteTimeSpan);
                return;
            }
        }

        _ = Task.Run(() => _taskManager.Execute<LibraryScanTask>());
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task RunScanAsync(IProgress<double> progress, CancellationToken ct)
    {
        var cfg       = Plugin.Instance!.Configuration;
        var languages = cfg.PreferredLanguages;

        if (!languages.Any())
        {
            _logger.LogWarning("No preferred languages configured — library scan aborted");
            return;
        }

        // Gather all movies + episodes without depending on broken GetRecursiveChildren
        var items = new List<BaseItem>();
        FindMediaRecursive(_libraryManager.RootFolder, items);

        var videos = items
            .Where(i => !string.IsNullOrEmpty(i.Path) && File.Exists(i.Path))
            .GroupBy(i => i.Id)
            .Select(g => g.First())
            .ToList();

        _logger.LogInformation("Library scan: {Count} video(s) found", videos.Count);

        int done = 0;
        foreach (var video in videos)
        {
            ct.ThrowIfCancellationRequested();

            foreach (var lang in languages)
            {
                if (SubtitleFileService.SubtitleExists(video.Path, lang))
                {
                    continue; // already have it
                }

                var entry = new ScanLogEntry
                {
                    ItemTitle = video.Name,
                    MediaPath = video.Path,
                    Language  = lang,
                };

                try
                {
                    var request = BuildRequest(video, lang);
                    var results = await _aggregator.SearchAsync(request, ct).ConfigureAwait(false);

                    if (!results.Any())
                    {
                        entry.Status = "NotFound";
                    }
                    else
                    {
                        var best     = results[0];
                        var download = await _fileService
                            .DownloadAndSaveAsync(best, video.Path, ct)
                            .ConfigureAwait(false);

                        entry.Status    = download.Success ? "Downloaded" : "Failed";
                        entry.SavedPath = download.SavedPath;
                        entry.Error     = download.Error;
                    }
                }
                catch (Exception ex)
                {
                    entry.Status = "Error";
                    entry.Error  = ex.Message;
                    _logger.LogWarning(ex, "Scan error for {Title} [{Lang}]", video.Name, lang);
                }

                AddToLog(entry);
            }

            done++;
            progress.Report((double)done / videos.Count * 100.0);
        }

        var logSnapshot = GetLastScanLog();
        _logger.LogInformation(
            "Library scan complete: {Downloaded} downloaded, {NotFound} not found, {Errors} errors",
            logSnapshot.Count(e => e.Status == "Downloaded"),
            logSnapshot.Count(e => e.Status == "NotFound"),
            logSnapshot.Count(e => e.Status is "Failed" or "Error"));
    }

    private void FindMediaRecursive(BaseItem root, List<BaseItem> items)
    {
        if (root is Video or Episode)
        {
            if (!string.IsNullOrEmpty(root.Path)) items.Add(root);
            return;
        }

        if (root is Folder folder)
        {
            foreach (var child in folder.Children)
            {
                FindMediaRecursive(child, items);
            }
        }
    }

    private static SubtitleSearchRequest BuildRequest(BaseItem item, string lang)
    {
        var req = new SubtitleSearchRequest
        {
            ItemId        = item.Id.ToString(),
            Title         = item.Name,
            Year          = item.ProductionYear,
            MediaFilePath = item.Path,
            Languages     = new List<string> { lang },
        };

        // IMDb ID
        var imdb = item.GetProviderId(MetadataProvider.Imdb);

        // Fallback to parent IMDb if episode doesn't have one
        if (string.IsNullOrEmpty(imdb) && item is Episode ep)
        {
            var series = ep.Series;
            imdb = series?.GetProviderId(MetadataProvider.Imdb);
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
}

/// <summary>One log entry from a library scan run.</summary>
public sealed class ScanLogEntry
{
    public string ItemTitle { get; set; } = string.Empty;
    public string MediaPath { get; set; } = string.Empty;
    public string Language  { get; set; } = string.Empty;
    public string Status    { get; set; } = string.Empty;  // Downloaded | NotFound | Failed | Error | Skipped
    public string SavedPath { get; set; } = string.Empty;
    public string? Error    { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
