using AngleSharp;
using AngleSharp.Html.Dom;
using IWEHZ.Infrastructure.Http;

namespace IWEHZ.Scrapers;

public sealed class ParariusScraper : IPropertyScraper
{
    private const string Url = "https://www.pararius.nl/huurwoningen/nederland";
    private readonly ScraperFetcher _fetcher;
    private readonly ILogger<ParariusScraper> _logger;

    public string SourceName => "pararius";

    public ParariusScraper(ScraperFetcher fetcher, ILogger<ParariusScraper> logger)
    {
        _fetcher = fetcher;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScrapedListing>> ScrapeAsync(CancellationToken ct)
    {
        // Pararius sits behind Cloudflare — render mode clears it; plain proxy gets a 500.
        // ultra_premium (30+ credits) is left off for the free plan — enable via
        // Scraper:ScraperApiUltraPremium:pararius if render alone stops working.
        using var http = _fetcher.CreateClient(SourceName, render: true);

        var html = await http.GetStringAsync(Url, ct);

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html), ct);

        var items = document.QuerySelectorAll("section.listing-search-item");
        if (items.Length == 0)
            throw new AllProxyAttemptsBlockedException(SourceName, 1, "no listings in response");

        var listings = new List<ScrapedListing>();

        foreach (var article in items)
        {
            try
            {
                var anchor = article.QuerySelector("a.listing-search-item__link--title") as IHtmlAnchorElement;
                if (anchor is null) continue;

                var href = anchor.Href ?? string.Empty;
                var externalId = ScraperHelpers.ExtractLastUrlSegment(href);
                if (string.IsNullOrEmpty(externalId)) continue;

                var title = anchor.TextContent.Trim();

                var city = article.QuerySelector(".listing-search-item__sub-title")?.TextContent.Trim() ?? string.Empty;
                var price = ScraperHelpers.ParsePrice(
                    article.QuerySelector(".listing-search-item__price")?.TextContent ?? string.Empty);
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
}
