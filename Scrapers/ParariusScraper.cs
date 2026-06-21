using AngleSharp;
using AngleSharp.Html.Dom;
using IWEHZ.Infrastructure.Http;
using Microsoft.Extensions.Configuration;

namespace IWEHZ.Scrapers;

public sealed class ParariusScraper : IPropertyScraper
{
    private const string BaseUrl = "https://www.pararius.nl/huurwoningen/nederland";
    private readonly string? _proxyUrl;
    private readonly ILogger<ParariusScraper> _logger;

    public string SourceName => "pararius";

    public ParariusScraper(Microsoft.Extensions.Configuration.IConfiguration config, ILogger<ParariusScraper> logger)
    {
        _proxyUrl = config["Scraper:ProxyUrl"];
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScrapedListing>> ScrapeAsync(CancellationToken ct)
    {
        using var http = ScraperHttpClientFactory.Create(_proxyUrl);

        string html;
        try
        {
            html = await http.GetStringAsync(BaseUrl, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pararius fetch failed");
            return [];
        }

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html), ct);

        var listings = new List<ScrapedListing>();

        foreach (var article in document.QuerySelectorAll("section.listing-search-item"))
        {
            try
            {
                var anchor = article.QuerySelector("a.listing-search-item__link--title") as IHtmlAnchorElement;
                if (anchor is null) continue;

                var href = anchor.Href ?? string.Empty;
                var externalId = ExtractExternalId(href);
                if (string.IsNullOrEmpty(externalId)) continue;

                var title = anchor.TextContent.Trim();

                var cityEl = article.QuerySelector(".listing-search-item__sub-title");
                var city = cityEl?.TextContent.Trim() ?? string.Empty;

                var priceEl = article.QuerySelector(".listing-search-item__price");
                var priceText = priceEl?.TextContent ?? string.Empty;
                var price = ParsePrice(priceText);
                if (price <= 0) continue;

                listings.Add(new ScrapedListing(externalId, title, city, price, href, SourceName));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Pararius: skipped malformed listing element");
            }
        }

        return listings;
    }

    private static string ExtractExternalId(string url)
    {
        var segments = url.TrimEnd('/').Split('/');
        return segments.Length > 0 ? segments[^1] : string.Empty;
    }

    private static decimal ParsePrice(string text)
    {
        var cleaned = new string(text.Where(c => char.IsDigit(c) || c == ',').ToArray())
            .Replace(",", string.Empty);
        return decimal.TryParse(cleaned, out var result) ? result : 0;
    }
}
