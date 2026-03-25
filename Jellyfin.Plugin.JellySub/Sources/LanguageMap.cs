using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellySub.Sources;

/// <summary>
/// Helpers for converting between language code formats used by different sites.
/// </summary>
public static class LanguageMap
{
    // BCP-47 → ISO 639-2/B (3-letter) — used by OpenSubtitles.org query params
    private static readonly Dictionary<string, string> TwoToThree = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "eng", ["fr"] = "fre", ["de"] = "ger", ["es"] = "spa",
        ["it"] = "ita", ["pt"] = "por", ["ru"] = "rus", ["ja"] = "jpn",
        ["ko"] = "kor", ["zh"] = "chi", ["ar"] = "ara", ["nl"] = "dut",
        ["pl"] = "pol", ["sv"] = "swe", ["no"] = "nor", ["da"] = "dan",
        ["fi"] = "fin", ["cs"] = "cze", ["sk"] = "slo", ["hu"] = "hun",
        ["ro"] = "rum", ["tr"] = "tur", ["el"] = "ell", ["he"] = "heb",
        ["uk"] = "ukr", ["bg"] = "bul", ["hr"] = "hrv", ["sr"] = "srp",
        ["ca"] = "cat", ["vi"] = "vie", ["th"] = "tha", ["id"] = "ind",
        ["ms"] = "may", ["fa"] = "per", ["hi"] = "hin",
    };

    // ISO 639-2/B → BCP-47 (reverse map — built lazily)
    private static readonly Dictionary<string, string> ThreeToTwo;

    // BCP-47 → human-readable name
    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = "English",  ["fr"] = "French",   ["de"] = "German",
        ["es"] = "Spanish",  ["it"] = "Italian",  ["pt"] = "Portuguese",
        ["ru"] = "Russian",  ["ja"] = "Japanese", ["ko"] = "Korean",
        ["zh"] = "Chinese",  ["ar"] = "Arabic",   ["nl"] = "Dutch",
        ["pl"] = "Polish",   ["sv"] = "Swedish",  ["no"] = "Norwegian",
        ["da"] = "Danish",   ["fi"] = "Finnish",  ["cs"] = "Czech",
        ["sk"] = "Slovak",   ["hu"] = "Hungarian", ["ro"] = "Romanian",
        ["tr"] = "Turkish",  ["el"] = "Greek",    ["he"] = "Hebrew",
        ["uk"] = "Ukrainian",["bg"] = "Bulgarian",["hr"] = "Croatian",
        ["sr"] = "Serbian",  ["ca"] = "Catalan",  ["vi"] = "Vietnamese",
        ["th"] = "Thai",     ["id"] = "Indonesian",["ms"] = "Malay",
        ["fa"] = "Persian",  ["hi"] = "Hindi",
    };

    static LanguageMap()
    {
        ThreeToTwo = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (two, three) in TwoToThree)
        {
            ThreeToTwo[three] = two;
        }
    }

    /// <summary>Convert BCP-47 code (e.g. "en") to ISO 639-2/B code (e.g. "eng").</summary>
    public static string ToThreeLetter(string twoLetter)
        => TwoToThree.TryGetValue(twoLetter, out var three) ? three : twoLetter;

    /// <summary>Convert ISO 639-2/B code (e.g. "eng") to BCP-47 code (e.g. "en").</summary>
    public static string ToTwoLetter(string threeLetter)
        => ThreeToTwo.TryGetValue(threeLetter, out var two) ? two : threeLetter;

    /// <summary>Return the human-readable name for a BCP-47 code.</summary>
    public static string DisplayName(string twoLetter)
        => DisplayNames.TryGetValue(twoLetter, out var name) ? name : twoLetter.ToUpperInvariant();
}
