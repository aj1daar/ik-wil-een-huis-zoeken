using System.Text.RegularExpressions;

namespace IWEHZ.Scrapers;

internal static partial class ScraperHelpers
{
    [GeneratedRegex(@"(\d{1,3}(?:[.,]\d{3})+(?:[.,]\d{2})?|\d+)")]
    private static partial Regex PriceRegex();

    internal static decimal ParsePrice(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;

        var normalised = text.Replace(" ", " ").Replace(" ", "");
        var match = PriceRegex().Match(normalised);
        if (!match.Success) return 0;

        var raw = match.Groups[1].Value;

        var lastSep = raw.LastIndexOfAny(['.', ',']);
        if (lastSep >= 0 && raw.Length - lastSep - 1 == 2)
            raw = raw[..lastSep];

        raw = raw.Replace(".", "").Replace(",", "");

        return decimal.TryParse(raw, out var result) && result > 0 ? result : 0;
    }

    internal static string ExtractLastUrlSegment(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return string.Empty;
        var trimmed = url.TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        return lastSlash >= 0 ? trimmed[(lastSlash + 1)..] : trimmed;
    }
}
