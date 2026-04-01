using Jellyfin.Plugin.JellySub.Services;
using Jellyfin.Plugin.JellySub.Sources;
using Jellyfin.Plugin.JellySub.Sync;
using Jellyfin.Plugin.JellySub.Tasks;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http;

namespace Jellyfin.Plugin.JellySub;

/// <summary>
/// Registers JellySub services into the Jellyfin DI container.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services, IServerApplicationHost host)
    {
        // Named HTTP client shared across all scrapers (browser-like UA, decompression on)
        services.AddHttpClient("JellySub", c =>
        {
            c.DefaultRequestHeaders.Add(
                "User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            c.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
        });

        // Subtitle sources (one per supported site)
        services.AddSingleton<ISubtitleSource, OpenSubtitlesOrgSource>();
        services.AddSingleton<ISubtitleSource, SubsceneSource>();
        services.AddSingleton<ISubtitleSource, YifySubtitlesSource>();

        // Sync tools (registered as both their concrete type and the interface)
        services.AddSingleton<FfsubsyncTool>();
        services.AddSingleton<AlassTool>();
        services.AddSingleton<ISyncTool>(sp => sp.GetRequiredService<FfsubsyncTool>());
        services.AddSingleton<ISyncTool>(sp => sp.GetRequiredService<AlassTool>());

        // Core services
        services.AddSingleton<SubtitleAggregator>();
        services.AddSingleton<SubtitleFileService>();
        services.AddSingleton<SeriesMatchingService>();
        services.AddSingleton<SyncToolManager>();

        // Scheduled task
        services.AddSingleton<IScheduledTask, LibraryScanTask>();
    }
}
