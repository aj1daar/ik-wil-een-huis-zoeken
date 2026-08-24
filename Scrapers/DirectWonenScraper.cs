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
        var sourceOverride = config[$"Scraper:SourceProxyUrl:{SourceName}"];
        _proxyUrl = string.IsNullOrWhiteSpace(sourceOverride) ? config["Scraper:ProxyUrl"] : sourceOverride;
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

        foreach (var tile in document.QuerySelectorAll("div.tile"))
        {
            try
            {
                var anchor = tile.QuerySelector("a.inner-content[href]") as IHtmlAnchorElement;
                if (anchor is null) continue;

                var rawHref = anchor.GetAttribute("href") ?? string.Empty;
                if (!TryParseCard(rawHref, out var externalId, out var listingUrl)) continue;

                var price = ScraperHelpers.ParsePrice(
                    tile.QuerySelector(".advert-location-price")?.TextContent ?? string.Empty);
                if (price <= 0) continue;

                var location = tile.QuerySelector("h3.location-text")?.TextContent.Trim() ?? string.Empty;
                var commaIdx = location.LastIndexOf(',');
                var city = commaIdx >= 0 ? location[(commaIdx + 1)..].Trim() : string.Empty;
                if (string.IsNullOrEmpty(city)) continue;

                var typeSpan = tile.QuerySelector("span.advert-location-header")?.TextContent.Trim() ?? string.Empty;
                var title = string.IsNullOrEmpty(typeSpan) ? location : $"{typeSpan} {location}";

                listings.Add(new ScrapedListing(externalId, title, city, price, listingUrl, SourceName));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "DirectWonen: skipped malformed listing");
            }
        }

        return listings;
    }

    private static bool TryParseCard(string rawHref, out string externalId, out string listingUrl)
    {
        externalId = string.Empty;
        listingUrl = string.Empty;

        // href is /premiumaccountpayment?ip=4&returnUrl={encoded listing url}&entityId={id}
        if (!rawHref.Contains("entityId=")) return false;

        var full = rawHref.StartsWith("http")
            ? rawHref
            : "https://directwonen.nl" + rawHref;

        if (!Uri.TryCreate(full, UriKind.Absolute, out var uri)) return false;

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var id = query["entityId"];
        var returnUrl = query["returnUrl"];

        if (string.IsNullOrEmpty(id) || !long.TryParse(id, out _)) return false;

        externalId = id;
        listingUrl = string.IsNullOrEmpty(returnUrl) ? full : returnUrl;
        return true;
    }
}
