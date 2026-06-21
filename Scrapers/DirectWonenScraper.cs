using AngleSharp;
using AngleSharp.Html.Dom;
using IWEHZ.Infrastructure.Http;

namespace IWEHZ.Scrapers;

public sealed class DirectWonenScraper : IPropertyScraper
{
    private const string BaseUrl = "https://directwonen.nl/huurwoningen-huren/nederland";
    private readonly string? _proxyUrl;
    private readonly ILogger<DirectWonenScraper> _logger;

    public string SourceName => "directwonen";

    public DirectWonenScraper(Microsoft.Extensions.Configuration.IConfiguration config, ILogger<DirectWonenScraper> logger)
    {
        _proxyUrl = config["Scraper:ProxyUrl"];
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScrapedListing>> ScrapeAsync(CancellationToken ct)
    {
        using var http = ScraperHttpClientFactory.Create(_proxyUrl);
        http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://directwonen.nl/");

        var html = await http.GetStringAsync(BaseUrl, ct);

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html), ct);

        var listings = new List<ScrapedListing>();

        foreach (var anchor in document.QuerySelectorAll("a[href]").OfType<IHtmlAnchorElement>())
        {
            try
            {
                var href = anchor.GetAttribute("href") ?? string.Empty;
                if (!TryParseListingHref(href, out var externalId)) continue;

                var price = ScraperHelpers.ParsePrice(anchor.TextContent);
                if (price <= 0) continue;

                // h3 contains "StreetName, City"
                var h3 = anchor.QuerySelector("h3");
                var h3Text = h3?.TextContent.Trim() ?? string.Empty;

                var commaIdx = h3Text.LastIndexOf(',');
                var city = commaIdx >= 0
                    ? h3Text[(commaIdx + 1)..].Trim()
                    : string.Empty;

                if (string.IsNullOrEmpty(city)) continue;

                listings.Add(new ScrapedListing(externalId, h3Text, city, price,
                    "https://directwonen.nl" + href, SourceName));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "DirectWonen: skipped malformed listing");
            }
        }

        return listings;
    }

    private static bool TryParseListingHref(string href, out string externalId)
    {
        externalId = string.Empty;

        // /huurwoningen-huren/{city}/{street}/{type}-{numericId}
        var parts = href.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || parts[0] != "huurwoningen-huren") return false;

        var lastSegment = parts[3];
        var dashIdx = lastSegment.LastIndexOf('-');
        if (dashIdx < 0) return false;

        var numeric = lastSegment[(dashIdx + 1)..];
        if (!long.TryParse(numeric, out _)) return false;

        externalId = numeric;
        return true;
    }
}
