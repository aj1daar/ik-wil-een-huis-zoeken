using AngleSharp;
using AngleSharp.Html.Dom;
using IWEHZ.Infrastructure.Http;

namespace IWEHZ.Scrapers;

public sealed class HuurwoningenScraper : IPropertyScraper
{
    private const string BaseUrl = "https://www.huurwoningen.nl/huurwoningen/";
    private readonly string? _proxyUrl;
    private readonly ILogger<HuurwoningenScraper> _logger;

    public string SourceName => "huurwoningen";

    public HuurwoningenScraper(IConfiguration config, ILogger<HuurwoningenScraper> logger)
    {
        _proxyUrl = config["Scraper:ProxyUrl"];
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScrapedListing>> ScrapeAsync(CancellationToken ct)
    {
        using var http = ScraperHttpClientFactory.Create(_proxyUrl);
        http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://www.huurwoningen.nl/");

        string html;
        try
        {
            html = await http.GetStringAsync(BaseUrl, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Huurwoningen fetch failed");
            return [];
        }

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html), ct);

        var listings = new List<ScrapedListing>();

        foreach (var card in document.QuerySelectorAll(".listing-search-item, .search-result__item, li[data-id]"))
        {
            try
            {
                var externalId = card.GetAttribute("data-id") ?? card.GetAttribute("data-listing-id");

                var anchor = card.QuerySelector("a[href*='/huurwoningen/']") as IHtmlAnchorElement;
                if (anchor is null) continue;

                var href = anchor.Href ?? string.Empty;

                if (string.IsNullOrEmpty(externalId))
                    externalId = ExtractIdFromUrl(href);

                if (string.IsNullOrEmpty(externalId)) continue;

                var titleEl = card.QuerySelector("h2, h3, .listing-search-item__title, .search-result__title");
                var title = titleEl?.TextContent.Trim() ?? anchor.TextContent.Trim();

                var cityEl = card.QuerySelector(".listing-search-item__location, .search-result__location, [class*='city'], [class*='location']");
                var city = cityEl?.TextContent.Trim() ?? string.Empty;

                var priceEl = card.QuerySelector("[class*='price'], .listing-search-item__price");
                var price = ParsePrice(priceEl?.TextContent ?? string.Empty);
                if (price <= 0) continue;

                listings.Add(new ScrapedListing(externalId, title, city, price, href, SourceName));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Huurwoningen: skipped malformed listing element");
            }
        }

        return listings;
    }

    private static string ExtractIdFromUrl(string url)
    {
        var segments = url.TrimEnd('/').Split('/');
        return segments.Length > 0 ? segments[^1] : string.Empty;
    }

    private static decimal ParsePrice(string text)
    {
        var cleaned = new string(text.Where(c => char.IsDigit(c)).ToArray());
        return decimal.TryParse(cleaned, out var result) ? result : 0;
    }
}
