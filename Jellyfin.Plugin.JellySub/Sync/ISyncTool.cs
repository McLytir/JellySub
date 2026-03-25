using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellySub.Models;

namespace Jellyfin.Plugin.JellySub.Sync;

/// <summary>
/// Contract for an external subtitle-sync tool (ffsubsync, alass …).
/// </summary>
public interface ISyncTool
{
    /// <summary>Stable identifier, e.g. "ffsubsync" or "alass".</summary>
    string Id { get; }

    /// <summary>Human-readable tool name.</summary>
    string DisplayName { get; }

    /// <summary>One-sentence description for the settings UI.</summary>
    string Description { get; }

    /// <summary>Check whether the tool is installed and return its status.</summary>
    SyncToolStatus GetStatus();

    /// <summary>
    /// Synchronise <paramref name="subtitlePath"/> to the reference video/subtitle and
    /// write the result to <paramref name="outputPath"/>.
    /// </summary>
    Task<SyncResult> SyncAsync(SyncRequest request, CancellationToken cancellationToken);
}
