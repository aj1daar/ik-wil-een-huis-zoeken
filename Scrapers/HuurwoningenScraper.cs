using System.Net;
using AngleSharp;
using AngleSharp.Html.Dom;
using IWEHZ.Infrastructure.Http;

namespace IWEHZ.Scrapers;

public sealed class HuurwoningenScraper : IPropertyScraper
{
    private static readonly string[] CitySlugs =
    [
        "amsterdam", "rotterdam", "utrecht", "eindhoven", "groningen",
        "tilburg", "almere", "breda", "nijmegen", "haarlem",
        "arnhem", "leiden", "maastricht", "delft", "dordrecht",
        "zwolle", "amersfoort", "alkmaar", "deventer",
    ];

    private const string BaseUrl = "https://www.huurwoningen.nl/in/";
    private readonly string? _proxyUrl;
    private readonly ILogger<HuurwoningenScraper> _logger;

    public string SourceName => "huurwoningen";

    public HuurwoningenScraper(Microsoft.Extensions.Configuration.IConfiguration config, ILogger<HuurwoningenScraper> logger)
    {
        var sourceOverride = config[$"Scraper:SourceProxyUrl:{SourceName}"];
        _proxyUrl = sourceOverride is not null ? sourceOverride : config["Scraper:ProxyUrl"];
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScrapedListing>> ScrapeAsync(CancellationToken ct)
    {
        var listings = new List<ScrapedListing>();
        var seen = new HashSet<string>();

        foreach (var city in CitySlugs)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var cityListings = await ScrapeCityAsync(city, ct);
                foreach (var listing in cityListings)
                {
                    if (seen.Add(listing.ExternalId))
                        listings.Add(listing);
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // city slug not supported by huurwoningen.nl — skip
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        return listings;
    }

    private async Task<List<ScrapedListing>> ScrapeCityAsync(string citySlug, CancellationToken ct)
    {
        using var http = ScraperHttpClientFactory.Create(_proxyUrl);
        http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://www.huurwoningen.nl/");

        var html = await http.GetStringAsync(BaseUrl + citySlug + "/", ct);

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
                    externalId = ScraperHelpers.ExtractLastUrlSegment(href);

                if (string.IsNullOrEmpty(externalId)) continue;

                var titleEl = card.QuerySelector("h2, h3, .listing-search-item__title, .search-result__title");
                var title = titleEl?.TextContent.Trim() ?? anchor.TextContent.Trim();

                var cityEl = card.QuerySelector(".listing-search-item__location, .search-result__location, [class*='city'], [class*='location']");
                var city = cityEl?.TextContent.Trim() ?? citySlug;

                var priceEl = card.QuerySelector("[class*='price'], .listing-search-item__price");
                var price = ScraperHelpers.ParsePrice(priceEl?.TextContent ?? string.Empty);
                if (price <= 0) continue;

                listings.Add(new ScrapedListing(externalId, title, city, price, href, SourceName));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Huurwoningen: skipped malformed listing");
            }
        }

        return listings;
    }
}
