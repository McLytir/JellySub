using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
    private static readonly Regex CueSeparatorRegex = new("\r?\n\r?\n+", RegexOptions.Compiled);
    private static readonly Regex BasicTagRegex = new("<(\\/?)(i|b|u)>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AssDefaultStyleRegex = new(@"^Style:\s*Default,[^\r\n]*$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

        var config = Plugin.Instance!.Configuration;
        var outputFormat = NormalizeOutputFormat(config.SubtitleOutputFormat);
        var destPath = BuildDestinationPath(mediaFilePath, result.Language, outputFormat);

        if (!config.OverwriteExisting && File.Exists(destPath))
        {
            _logger.LogInformation("Skipping {Path} — subtitle already exists", destPath);
            return new DownloadedSubtitle
            {
                Success   = true,
                SavedPath = destPath,
                Language  = result.Language
            };
        }

        var outputContent = outputFormat == "Ass"
            ? ConvertSrtToAss(srtContent, config.StyledSubtitleFontFamily, config.StyledSubtitleFontSize)
            : srtContent;

        try
        {
            await File.WriteAllTextAsync(destPath, outputContent, cancellationToken).ConfigureAwait(false);
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
        var format = NormalizeOutputFormat(Plugin.Instance?.Configuration.SubtitleOutputFormat);
        var path = BuildDestinationPath(mediaFilePath, language, format);
        return File.Exists(path);
    }

    /// <summary>
    /// Build the canonical subtitle path for a media file.
    /// E.g.: /media/The.Matrix.mkv + "en" → /media/The.Matrix.en.srt
    /// or /media/The.Matrix.en.ass depending on the configured output format.
    /// </summary>
    public static string BuildDestinationPath(string mediaFilePath, string language, string? outputFormat = null)
    {
        var dir  = Path.GetDirectoryName(mediaFilePath)!;
        var stem = Path.GetFileNameWithoutExtension(mediaFilePath);
        var ext = NormalizeOutputFormat(outputFormat) == "Ass" ? "ass" : "srt";
        return Path.Combine(dir, $"{stem}.{language}.{ext}");
    }

    /// <summary>
    /// Restyle existing subtitle sidecar files for a media item using the configured
    /// font family and font size. SRT files are converted to ASS; existing ASS files
    /// have their default style updated in place.
    /// </summary>
    public async Task<IReadOnlyList<RestyledSubtitleFile>> RestyleExistingSubtitlesAsync(
        string mediaFilePath,
        bool replaceOriginalSrt,
        CancellationToken cancellationToken)
    {
        var dir = Path.GetDirectoryName(mediaFilePath);
        var stem = Path.GetFileNameWithoutExtension(mediaFilePath);
        if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(stem) || !Directory.Exists(dir))
        {
            return Array.Empty<RestyledSubtitleFile>();
        }

        var config = Plugin.Instance!.Configuration;
        var candidates = Directory.EnumerateFiles(dir, $"{stem}.*")
            .Where(path => path.StartsWith(Path.Combine(dir, stem + "."), StringComparison.OrdinalIgnoreCase))
            .Where(path =>
            {
                var ext = Path.GetExtension(path);
                return ext.Equals(".srt", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".ass", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(path => path)
            .ToList();

        var results = new List<RestyledSubtitleFile>();
        foreach (var sourcePath in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await RestyleSubtitleFileAsync(
                sourcePath,
                config.StyledSubtitleFontFamily,
                config.StyledSubtitleFontSize,
                replaceOriginalSrt,
                cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    private static string NormalizeOutputFormat(string? outputFormat)
        => string.Equals(outputFormat, "Ass", StringComparison.OrdinalIgnoreCase) ? "Ass" : "Srt";

    public static string ConvertSrtToAss(string srtContent, string? fontFamily, int fontSize)
    {
        var safeFont = string.IsNullOrWhiteSpace(fontFamily) ? "Arial" : fontFamily.Trim();
        var safeSize = Math.Clamp(fontSize, 8, 120);
        var normalized = srtContent.Replace("\r\n", "\n").Trim();
        var builder = new StringBuilder();

        builder.AppendLine("[Script Info]");
        builder.AppendLine("ScriptType: v4.00+");
        builder.AppendLine("WrapStyle: 0");
        builder.AppendLine("ScaledBorderAndShadow: yes");
        builder.AppendLine();
        builder.AppendLine("[V4+ Styles]");
        builder.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
        builder.AppendLine(BuildAssDefaultStyleLine(safeFont, safeSize));
        builder.AppendLine();
        builder.AppendLine("[Events]");
        builder.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

        foreach (var block in CueSeparatorRegex.Split(normalized))
        {
            var lines = block.Split('\n', StringSplitOptions.None)
                .Select(l => l.TrimEnd())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();
            if (!lines.Any())
            {
                continue;
            }

            var timingIndex = lines.FindIndex(l => l.Contains("-->", StringComparison.Ordinal));
            if (timingIndex < 0)
            {
                continue;
            }

            var timingParts = lines[timingIndex].Split("-->", StringSplitOptions.TrimEntries);
            if (timingParts.Length != 2)
            {
                continue;
            }

            var textLines = lines.Skip(timingIndex + 1).ToList();
            if (!textLines.Any())
            {
                textLines.Add(string.Empty);
            }

            var text = string.Join("\\N", textLines.Select(ConvertInlineMarkupToAss));
            builder.Append("Dialogue: 0,");
            builder.Append(ConvertSrtTimeToAss(timingParts[0]));
            builder.Append(',');
            builder.Append(ConvertSrtTimeToAss(timingParts[1]));
            builder.AppendLine($",Default,,0,0,0,,{text}");
        }

        return builder.ToString();
    }

    private static string ConvertSrtTimeToAss(string value)
    {
        var normalized = value.Trim().Replace(',', '.');
        if (TimeSpan.TryParseExact(normalized, @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture, out var ts))
        {
            var totalHours = (int)ts.TotalHours;
            var centiseconds = ts.Milliseconds / 10;
            return $"{totalHours}:{ts.Minutes:00}:{ts.Seconds:00}.{centiseconds:00}";
        }

        return normalized;
    }

    private static string ConvertInlineMarkupToAss(string value)
    {
        var escaped = value
            .Replace("\\", "\\\\")
            .Replace("{", "\\{")
            .Replace("}", "\\}");

        escaped = BasicTagRegex.Replace(escaped, match =>
        {
            var closing = match.Groups[1].Value == "/";
            var tag = match.Groups[2].Value.ToLowerInvariant();
            var state = closing ? "0" : "1";
            return tag switch
            {
                "i" => $"{{\\i{state}}}",
                "b" => $"{{\\b{state}}}",
                "u" => $"{{\\u{state}}}",
                _ => string.Empty,
            };
        });

        return escaped;
    }

    private static async Task<RestyledSubtitleFile> RestyleSubtitleFileAsync(
        string sourcePath,
        string? fontFamily,
        int fontSize,
        bool replaceOriginalSrt,
        CancellationToken cancellationToken)
    {
        try
        {
            var ext = Path.GetExtension(sourcePath);
            var content = await File.ReadAllTextAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            string outputPath;
            string outputContent;

            if (ext.Equals(".srt", StringComparison.OrdinalIgnoreCase))
            {
                outputPath = Path.ChangeExtension(sourcePath, ".ass");
                outputContent = ConvertSrtToAss(content, fontFamily, fontSize);
            }
            else if (ext.Equals(".ass", StringComparison.OrdinalIgnoreCase))
            {
                outputPath = sourcePath;
                outputContent = RestyleAssContent(content, fontFamily, fontSize);
            }
            else
            {
                return new RestyledSubtitleFile
                {
                    SourcePath = sourcePath,
                    SavedPath = sourcePath,
                    Success = false,
                    Status = "Failed",
                    Error = "Unsupported subtitle format",
                };
            }

            await File.WriteAllTextAsync(outputPath, outputContent, cancellationToken).ConfigureAwait(false);

            if (replaceOriginalSrt && ext.Equals(".srt", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(sourcePath, outputPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(sourcePath);
            }

            return new RestyledSubtitleFile
            {
                SourcePath = sourcePath,
                SavedPath = outputPath,
                Success = true,
                Status = "Restyled",
            };
        }
        catch (Exception ex)
        {
            return new RestyledSubtitleFile
            {
                SourcePath = sourcePath,
                SavedPath = string.Empty,
                Success = false,
                Status = "Failed",
                Error = ex.Message,
            };
        }
    }

    private static string RestyleAssContent(string assContent, string? fontFamily, int fontSize)
    {
        var styleLine = BuildAssDefaultStyleLine(fontFamily, fontSize);
        if (AssDefaultStyleRegex.IsMatch(assContent))
        {
            return AssDefaultStyleRegex.Replace(assContent, styleLine, 1);
        }

        const string marker = "[V4+ Styles]";
        var formatIndex = assContent.IndexOf("Format:", StringComparison.OrdinalIgnoreCase);
        if (assContent.Contains(marker, StringComparison.OrdinalIgnoreCase) && formatIndex >= 0)
        {
            var lineEnd = assContent.IndexOf('\n', formatIndex);
            if (lineEnd >= 0)
            {
                return assContent.Insert(lineEnd + 1, styleLine + Environment.NewLine);
            }
        }

        return assContent + Environment.NewLine + marker + Environment.NewLine
            + "Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding" + Environment.NewLine
            + styleLine + Environment.NewLine;
    }

    private static string BuildAssDefaultStyleLine(string? fontFamily, int fontSize)
    {
        var safeFont = string.IsNullOrWhiteSpace(fontFamily) ? "Arial" : fontFamily.Trim();
        var safeSize = Math.Clamp(fontSize, 8, 120);
        return $"Style: Default,{EscapeAssStyleValue(safeFont)},{safeSize},&H00FFFFFF,&H000000FF,&H00000000,&H64000000,0,0,0,0,100,100,0,0,1,2,1,2,20,20,24,1";
    }

    private static string EscapeAssStyleValue(string value)
        => value.Replace(',', ' ');

    private static DownloadedSubtitle Fail(string error) =>
        new() { Success = false, Error = error };
}
