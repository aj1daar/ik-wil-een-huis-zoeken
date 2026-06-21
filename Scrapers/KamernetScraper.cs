using AngleSharp;
using AngleSharp.Html.Dom;
using IWEHZ.Infrastructure.Http;

namespace IWEHZ.Scrapers;

public sealed class KamernetScraper : IPropertyScraper
{
    private const string BaseUrl = "https://www.kamernet.nl/huren/appartement-nederland";
    private readonly string? _proxyUrl;
    private readonly ILogger<KamernetScraper> _logger;

    public string SourceName => "kamernet";

    public KamernetScraper(Microsoft.Extensions.Configuration.IConfiguration config, ILogger<KamernetScraper> logger)
    {
        _proxyUrl = config["Scraper:ProxyUrl"];
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScrapedListing>> ScrapeAsync(CancellationToken ct)
    {
        using var http = ScraperHttpClientFactory.Create(_proxyUrl);
        http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://www.kamernet.nl/");

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

                var price = ScraperHelpers.ParsePrice(anchor.TextContent);
                if (price <= 0) continue;

                var img = anchor.QuerySelector("img");
                var title = img?.GetAttribute("alt")?.Trim()
                    ?? $"Huurwoning {city}";

                listings.Add(new ScrapedListing(externalId, title, city, price,
                    "https://www.kamernet.nl" + href, SourceName));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Kamernet: skipped malformed listing");
            }
        }

        return listings;
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
