using System;
using System.Collections.Generic;
using Jellyfin.Plugin.JellySub.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.JellySub;

/// <summary>
/// JellySub plugin entry point.
/// </summary>
public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>Plugin GUID — must stay constant across releases.</summary>
    public static readonly Guid PluginGuid = Guid.Parse("a8b56a08-4e33-4a8c-b3e1-24e3b8b1e6c1");

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "JellySub";

    /// <inheritdoc />
    public override Guid Id => PluginGuid;

    /// <summary>Gets the singleton plugin instance.</summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        var ns = GetType().Namespace!;
        return new[]
        {
            // Settings / entry-point page
            new PluginPageInfo { Name = "jellysub", DisplayName = "JellySub Settings", EnableInMainMenu = true, MenuSection = "plugins", MenuIcon = "settings", EmbeddedResourcePath = $"{ns}.Web.config.html" },
            new PluginPageInfo { Name = "jellysubjs",       EmbeddedResourcePath = $"{ns}.Web.config.js"   },

            // Unified operations page (single search + batch + scan) on a fresh route/controller to defeat stale client caches
            new PluginPageInfo { Name = "jellysubsearchv4",   DisplayName = "JellySub Operations", EnableInMainMenu = true, MenuSection = "plugins", MenuIcon = "subtitles", EmbeddedResourcePath = $"{ns}.Web.search.html" },
            new PluginPageInfo { Name = "jellysubsearchjsv4", EmbeddedResourcePath = $"{ns}.Web.search.js"   },

            // Legacy routes kept for backward-compatible redirects into the unified page
            new PluginPageInfo { Name = "jellysubsearchv3",          DisplayName = "JellySub Operations (Legacy)", EnableInMainMenu = false, MenuSection = "plugins", MenuIcon = "subtitles", EmbeddedResourcePath = $"{ns}.Web.search_redirect.html" },
            new PluginPageInfo { Name = "jellysubsearchredirectjsv3", EmbeddedResourcePath = $"{ns}.Web.search_redirect.js"   },
            new PluginPageInfo { Name = "jellysubseries",            DisplayName = "Batch Subtitles", EnableInMainMenu = false, MenuSection = "plugins", MenuIcon = "folder", EmbeddedResourcePath = $"{ns}.Web.series.html" },
            new PluginPageInfo { Name = "jellysubseriesjs",          EmbeddedResourcePath = $"{ns}.Web.series.js"   },
            new PluginPageInfo { Name = "jellysubscan",              DisplayName = "Subtitle Scan", EnableInMainMenu = false, MenuSection = "plugins", MenuIcon = "search", EmbeddedResourcePath = $"{ns}.Web.scan.html"   },
            new PluginPageInfo { Name = "jellysubscanjs",            EmbeddedResourcePath = $"{ns}.Web.scan.js"     },
        };
    }
}
