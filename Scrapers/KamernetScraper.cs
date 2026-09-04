using System.Text.RegularExpressions;
using AngleSharp;
using AngleSharp.Html.Dom;
using IWEHZ.Infrastructure.Http;

namespace IWEHZ.Scrapers;

public sealed class KamernetScraper : IPropertyScraper
{
    private const string BaseUrl = "https://kamernet.nl/huren/appartement-nederland";
    private readonly ScraperFetcher _fetcher;
    private readonly ILogger<KamernetScraper> _logger;

    public string SourceName => "kamernet";

    public KamernetScraper(ScraperFetcher fetcher, ILogger<KamernetScraper> logger)
    {
        _fetcher = fetcher;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScrapedListing>> ScrapeAsync(CancellationToken ct)
    {
        using var http = _fetcher.CreateClient(SourceName);
        http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://kamernet.nl/");

        var html = await http.GetStringAsync(BaseUrl, ct);

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html), ct);

        var listings = new List<ScrapedListing>();

        foreach (var anchor in document.QuerySelectorAll("a[href]").OfType<IHtmlAnchorElement>())
        {
            try
            {
                var href = anchor.GetAttribute("href") ?? string.Empty;
                if (!TryParseListingHref(href, out var externalId, out var city)) continue;

                // The card's own text (m², room count, ...) has no price — the rent only
                // shows up in the thumbnail's alt text, e.g. "Appartement te huur 1350 euro Zwaanshals".
                var img = anchor.QuerySelector("img");
                var title = img?.GetAttribute("alt")?.Trim()
                    ?? $"Huurwoning {city}";

                var price = ParsePriceFromTitle(title);
                if (price <= 0) continue;

                listings.Add(new ScrapedListing(externalId, title, city, price,
                    "https://kamernet.nl" + href, SourceName));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Kamernet: skipped malformed listing");
            }
        }

        return listings;
    }

    internal static decimal ParsePriceFromTitle(string title)
    {
        var match = Regex.Match(title, @"(\d[\d.,]*)\s*euro", RegexOptions.IgnoreCase);
        return match.Success ? ScraperHelpers.ParsePrice(match.Groups[1].Value) : 0;
    }

    private static bool TryParseListingHref(string href, out string externalId, out string city)
    {
        externalId = string.Empty;
        city = string.Empty;

        // Pattern: /huren/{type}-{city}/{street}/{type}-{numericId}
        var parts = href.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || parts[0] != "huren") return false;

        var lastSegment = parts[3];
        var dashIdx = lastSegment.LastIndexOf('-');
        if (dashIdx < 0) return false;

        var numeric = lastSegment[(dashIdx + 1)..];
        if (!long.TryParse(numeric, out _)) return false;
        externalId = numeric;

        // parts[1] = "appartement-amsterdam" → strip the type prefix
        var typeDash = parts[1].IndexOf('-');
        if (typeDash < 0) return false;
        city = parts[1][(typeDash + 1)..].Replace('-', ' ');

        return true;
    }
}
