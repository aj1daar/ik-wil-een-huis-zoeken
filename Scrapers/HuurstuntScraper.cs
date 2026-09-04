using System.Text.Json;
using AngleSharp;
using IWEHZ.Infrastructure.Http;

namespace IWEHZ.Scrapers;

public sealed class HuurstuntScraper : IPropertyScraper
{
    private const string BaseUrl = "https://www.huurstunt.nl/huren/nederland";
    private const string Source = "huurstunt";
    private readonly ScraperFetcher _fetcher;
    private readonly ILogger<HuurstuntScraper> _logger;

    public string SourceName => Source;

    public HuurstuntScraper(ScraperFetcher fetcher, ILogger<HuurstuntScraper> logger)
    {
        _fetcher = fetcher;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScrapedListing>> ScrapeAsync(CancellationToken ct)
    {
        using var http = _fetcher.CreateClient(SourceName);
        http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://www.huurstunt.nl/");

        var html = await http.GetStringAsync(BaseUrl, ct);

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html), ct);

        var listings = new List<ScrapedListing>();
        var seen = new HashSet<string>();

        foreach (var script in document.QuerySelectorAll("script[type='application/ld+json']"))
        {
            var json = script.TextContent;
            if (string.IsNullOrWhiteSpace(json) || !json.Contains("RealEstateListing", StringComparison.Ordinal))
                continue;

            foreach (var item in ExtractListingElements(json))
            {
                try
                {
                    if (TryParseListing(item, out var listing) && seen.Add(listing.ExternalId))
                        listings.Add(listing);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Huurstunt: skipped malformed listing");
                }
            }
        }

        return listings;
    }

    // The site embeds its search results as schema.org JSON-LD (WebPage -> mainContentOfPage
    // -> about -> mainEntity.itemListElement[]) for SEO. That's more reliable than the
    // JS-hydrated card markup, which isn't present in the raw HTML response.
    internal static IEnumerable<JsonElement> ExtractListingElements(string json)
    {
        JsonElement root;
        try
        {
            root = JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch (JsonException)
        {
            yield break;
        }

        if (!root.TryGetProperty("mainContentOfPage", out var main) ||
            !main.TryGetProperty("about", out var about) ||
            !about.TryGetProperty("mainEntity", out var mainEntity) ||
            !mainEntity.TryGetProperty("itemListElement", out var items) ||
            items.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var listItem in items.EnumerateArray())
        {
            if (listItem.TryGetProperty("item", out var item))
                yield return item;
        }
    }

    internal static bool TryParseListing(JsonElement item, out ScrapedListing listing)
    {
        listing = null!;

        if (!item.TryGetProperty("url", out var urlProp) || urlProp.ValueKind != JsonValueKind.String)
            return false;
        var url = urlProp.GetString() ?? string.Empty;

        var externalId = ScraperHelpers.ExtractLastUrlSegment(url);
        if (string.IsNullOrEmpty(externalId))
            return false;

        if (!item.TryGetProperty("offers", out var offers) || !offers.TryGetProperty("price", out var priceProp))
            return false;

        var price = priceProp.ValueKind switch
        {
            JsonValueKind.Number => priceProp.GetDecimal(),
            JsonValueKind.String => ScraperHelpers.ParsePrice(priceProp.GetString() ?? string.Empty),
            _ => 0m,
        };
        if (price <= 0) return false;

        if (!TryParsePath(url, out var propertyType, out var city, out var street))
            return false;

        var title = string.IsNullOrEmpty(street) ? $"{propertyType} {city}" : $"{propertyType} {street}";

        listing = new ScrapedListing(externalId, title, city, price, url, Source);
        return true;
    }

    // URL shape: https://www.huurstunt.nl/{type}/huren/in/{city}/{street}/{id}
    private static bool TryParsePath(string url, out string propertyType, out string city, out string street)
    {
        propertyType = string.Empty;
        city = string.Empty;
        street = string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;

        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 6 || parts[1] != "huren" || parts[2] != "in") return false;

        propertyType = Capitalize(parts[0]);
        city = parts[3].Replace('-', ' ');
        street = Capitalize(parts[4].Replace('-', ' '));
        return true;
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
