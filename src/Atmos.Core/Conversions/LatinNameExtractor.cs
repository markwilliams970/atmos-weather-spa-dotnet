using System.Text.RegularExpressions;

namespace Atmos.Core.Conversions;

/// <summary>
/// Ported from latinName() (weather-server.ts). OSM "name" tags are frequently
/// local-script-only (Chinese, Arabic, Devanagari, Tibetan…) with no "name:en"
/// tag — this pulls out the leading Latin-script run of a string (handles OSM's
/// common "Latin Name  Local-script Name" concatenation pattern) and rejects
/// names with no usable Latin portion.
///
/// .NET's regex engine has no direct equivalent of JS's \p{Script=Latin} Unicode
/// script property — the closest built-in is named Unicode *blocks*, which is a
/// close but not pixel-perfect approximation (it also matches a few non-letter
/// symbols within those blocks). Acceptable here: this is a cosmetic label
/// enhancement, not a security- or correctness-critical parse.
/// </summary>
public static partial class LatinNameExtractor
{
    [GeneratedRegex(
        @"^[\p{IsBasicLatin}\p{IsLatin-1Supplement}\p{IsLatinExtended-A}\p{IsLatinExtended-B}0-9]" +
        @"[\p{IsBasicLatin}\p{IsLatin-1Supplement}\p{IsLatinExtended-A}\p{IsLatinExtended-B}0-9'’.,()\- ]*")]
    private static partial Regex LeadingLatinRun();

    public static string? Extract(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return null;
        }

        var match = LeadingLatinRun().Match(s);
        var cleaned = match.Success ? match.Value.Trim() : null;
        return !string.IsNullOrEmpty(cleaned) && cleaned.Length >= 2 ? cleaned : null;
    }
}
