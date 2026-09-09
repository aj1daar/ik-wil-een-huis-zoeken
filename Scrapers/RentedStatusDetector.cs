using System.Text.RegularExpressions;

namespace IWEHZ.Scrapers;

/// <summary>
/// Recognises the "this one is gone" wording Dutch rental sites put on a listing that has
/// already been let: "Verhuurd", "Verhuurd onder voorbehoud", "Onder optie", "Gereserveerd",
/// "Niet meer beschikbaar", plus the English equivalents used by the bilingual sites.
///
/// Matching is word-bounded on purpose: "verhuurder" (landlord) and "verhuurmakelaar"
/// (letting agent) appear on nearly every active listing and must not count as rented.
/// A marker preceded by "niet" ("nog niet verhuurd") is a negation and is ignored.
/// </summary>
public static partial class RentedStatusDetector
{
    [GeneratedRegex(
        """
        (?<!\bniet\s)\b(?:
            verhuurd
          | gereserveerd
          | (?:onder|in)\s+optie
          | onder\s+bod
          | vergeven
          | bezet
          | verkocht
          | niet\s+(?:meer\s+|langer\s+)?beschikbaar
          | no\s+longer\s+available
          | not\s+available
          | unavailable
          | rented(?:\s+out)?
          | let\s+agreed
          | under\s+offer
          | sold
        )\b
        """,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex MarkerRegex();

    // schema.org availability values that mean the offer is closed. Sites that publish
    // JSON-LD (huurstunt) flip this instead of changing the visible card text.
    private static readonly string[] UnavailableSchemaValues =
        ["soldout", "outofstock", "discontinued", "instoreonly"];

    public static bool IsRented(string? text) => TryMatch(text, out _);

    public static bool TryMatch(string? text, out string? marker)
    {
        marker = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var match = MarkerRegex().Match(text);
        if (!match.Success) return false;

        marker = match.Value.Trim();
        return true;
    }

    /// <summary>True when a schema.org <c>offers.availability</c> value means "no longer offered".</summary>
    public static bool IsUnavailableSchemaValue(string? availability)
    {
        if (string.IsNullOrWhiteSpace(availability)) return false;

        // Value is a URL or bare enum name: "https://schema.org/SoldOut", "SoldOut", "schema:SoldOut".
        var tail = availability.Trim().TrimEnd('/');
        var lastSep = tail.LastIndexOfAny(['/', ':', '#']);
        if (lastSep >= 0) tail = tail[(lastSep + 1)..];

        return UnavailableSchemaValues.Contains(tail.ToLowerInvariant());
    }

    /// <summary>Rented check for a freshly scraped listing: its status label first, then its title.</summary>
    public static bool IsRented(ScrapedListing listing) =>
        TryMatch(listing.StatusText, out _)
        || IsUnavailableSchemaValue(listing.StatusText)
        || TryMatch(listing.Title, out _);
}
