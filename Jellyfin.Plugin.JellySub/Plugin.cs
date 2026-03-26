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

            // Subtitle search (manual + assisted)
            new PluginPageInfo { Name = "jellysubsearch",   DisplayName = "Subtitle Search", EnableInMainMenu = true, MenuSection = "plugins", MenuIcon = "subtitles", EmbeddedResourcePath = $"{ns}.Web.search.html" },
            new PluginPageInfo { Name = "jellysubsearchjs", EmbeddedResourcePath = $"{ns}.Web.search.js"   },

            // Series / folder batch download
            new PluginPageInfo { Name = "jellysubseries",   DisplayName = "Batch Subtitles", EnableInMainMenu = true, MenuSection = "plugins", MenuIcon = "folder", EmbeddedResourcePath = $"{ns}.Web.series.html" },
            new PluginPageInfo { Name = "jellysubseriesjs", EmbeddedResourcePath = $"{ns}.Web.series.js"   },

            // Library scan
            new PluginPageInfo { Name = "jellysubscan",     DisplayName = "Subtitle Scan", EnableInMainMenu = true, MenuSection = "plugins", MenuIcon = "search", EmbeddedResourcePath = $"{ns}.Web.scan.html"   },
            new PluginPageInfo { Name = "jellysubscanjs",   EmbeddedResourcePath = $"{ns}.Web.scan.js"     },
        };
    }
}
