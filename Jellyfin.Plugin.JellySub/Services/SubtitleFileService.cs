using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.JellySub.Models;
using Jellyfin.Plugin.JellySub.Sources;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellySub.Services;

/// <summary>
/// Downloads a subtitle from its source and writes it to disk alongside the
/// media file, using Jellyfin-compatible naming conventions.
/// </summary>
public sealed class SubtitleFileService
{
    private readonly IEnumerable<ISubtitleSource> _sources;
    private readonly ILogger<SubtitleFileService> _logger;

    /// <summary>Initializes the subtitle file service.</summary>
    public SubtitleFileService(
        IEnumerable<ISubtitleSource> sources,
        ILogger<SubtitleFileService> logger)
    {
        _sources = sources;
        _logger  = logger;
    }

    /// <summary>
    /// Download <paramref name="result"/> and save it next to <paramref name="mediaFilePath"/>.
    /// </summary>
    public async Task<DownloadedSubtitle> DownloadAndSaveAsync(
        SubtitleResult result,
        string mediaFilePath,
        CancellationToken cancellationToken)
    {
        var source = _sources.FirstOrDefault(s => s.Id == result.SourceId);
        if (source is null)
        {
            return Fail($"Unknown source '{result.SourceId}'");
        }

        string? srtContent;
        try
        {
            srtContent = await source.DownloadAsync(result, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download error from {Source} for {Url}", result.SourceId, result.DownloadUrl);
            return Fail($"Download error: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(srtContent))
        {
            return Fail("Source returned empty subtitle content");
        }

        var destPath = BuildDestinationPath(mediaFilePath, result.Language);

        if (!Plugin.Instance!.Configuration.OverwriteExisting && File.Exists(destPath))
        {
            _logger.LogInformation("Skipping {Path} — subtitle already exists", destPath);
            return new DownloadedSubtitle
            {
                Success   = true,
                SavedPath = destPath,
                Language  = result.Language
            };
        }

        try
        {
            await File.WriteAllTextAsync(destPath, srtContent, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write subtitle to {Path}", destPath);
            return Fail($"File write error: {ex.Message}");
        }

        _logger.LogInformation("Saved subtitle → {Path}", destPath);
        return new DownloadedSubtitle
        {
            Success   = true,
            SavedPath = destPath,
            Language  = result.Language
        };
    }

    /// <summary>
    /// Returns true if a subtitle for <paramref name="language"/> already exists
    /// next to <paramref name="mediaFilePath"/>.
    /// </summary>
    public static bool SubtitleExists(string mediaFilePath, string language)
    {
        var path = BuildDestinationPath(mediaFilePath, language);
        return File.Exists(path);
    }

    /// <summary>
    /// Build the canonical subtitle path for a media file.
    /// E.g.: /media/The.Matrix.mkv + "en" → /media/The.Matrix.en.srt
    /// </summary>
    public static string BuildDestinationPath(string mediaFilePath, string language)
    {
        var dir  = Path.GetDirectoryName(mediaFilePath)!;
        var stem = Path.GetFileNameWithoutExtension(mediaFilePath);
        return Path.Combine(dir, $"{stem}.{language}.srt");
    }

    private static DownloadedSubtitle Fail(string error) =>
        new() { Success = false, Error = error };
}
