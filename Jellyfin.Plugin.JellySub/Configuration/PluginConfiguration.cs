using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.JellySub.Configuration;

/// <summary>
/// Persistent plugin configuration stored in Jellyfin's config directory.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    // ── Sources ───────────────────────────────────────────────────────────────

    /// <summary>Ordered list of enabled source IDs (first = highest priority).</summary>
    public List<string> EnabledSources { get; set; } = new()
    {
        SourceIds.OpenSubtitlesOrg,
        SourceIds.Subscene,
        SourceIds.YifySubtitles
    };

    // ── Languages ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Preferred subtitle languages as BCP-47 codes, in priority order.
    /// Example: ["en", "fr", "de"]
    /// </summary>
    public List<string> PreferredLanguages { get; set; } = new() { "en" };

    /// <summary>If true and no preferred-language subtitle is found, accept any language.</summary>
    public bool FallbackToAnyLanguage { get; set; } = false;

    // ── Download behaviour ────────────────────────────────────────────────────

    /// <summary>
    /// What happens when the user clicks a Jellyfin media item.
    /// "Assisted" = auto-search, manual selection.
    /// "Auto"     = silent best-match download.
    /// </summary>
    public string DefaultItemMode { get; set; } = "Assisted";

    /// <summary>
    /// Overwrite an existing subtitle file if one is already present.
    /// </summary>
    public bool OverwriteExisting { get; set; } = false;

    /// <summary>
    /// Skip search results with fewer than this many downloads (0 = accept all).
    /// </summary>
    public int MinimumDownloadCount { get; set; } = 0;

    // ── Library scan ──────────────────────────────────────────────────────────

    /// <summary>
    /// When to run the automated library scan.
    /// "Manual" | "AfterLibraryRefresh" | "Daily" | "Weekly"
    /// </summary>
    public string BatchScanSchedule { get; set; } = "Manual";

    // ── Sync tools ────────────────────────────────────────────────────────────

    /// <summary>
    /// Absolute path to the ffsubsync executable (or empty to use PATH).
    /// </summary>
    public string FfsubsyncPath { get; set; } = string.Empty;

    /// <summary>
    /// Absolute path to the alass executable (or empty to use PATH).
    /// </summary>
    public string AlassPath { get; set; } = string.Empty;

    /// <summary>
    /// Auto-sync mode applied after every download.
    /// "Off" | "Ffsubsync" | "Alass"
    /// </summary>
    public string AutoSyncAfterDownload { get; set; } = "Off";

    /// <summary>
    /// When syncing, keep the original subtitle alongside the synced copy.
    /// </summary>
    public bool SyncKeepOriginal { get; set; } = true;
}
