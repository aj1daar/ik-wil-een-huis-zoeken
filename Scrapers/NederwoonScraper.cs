using System.Net;
using AngleSharp;
using AngleSharp.Html.Dom;
using IWEHZ.Infrastructure.Http;

namespace IWEHZ.Scrapers;

public sealed class NederwoonScraper : IPropertyScraper
{
    private static readonly string[] CitySlugs =
    [
        "amsterdam", "rotterdam", "utrecht", "haarlem", "leiden",
        "delft", "breda", "tilburg", "nijmegen", "arnhem",
        "maastricht", "zwolle", "almere", "eindhoven", "groningen",
        "dordrecht", "alkmaar", "amersfoort",
    ];

    private const string BaseUrl = "https://www.nederwoon.nl/huurwoningen/";
    private readonly string? _proxyUrl;
    private readonly ILogger<NederwoonScraper> _logger;

    public string SourceName => "nederwoon";

    public NederwoonScraper(Microsoft.Extensions.Configuration.IConfiguration config, ILogger<NederwoonScraper> logger)
    {
        _proxyUrl = config["Scraper:ProxyUrl"];
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScrapedListing>> ScrapeAsync(CancellationToken ct)
    {
        var listings = new List<ScrapedListing>();
        var seen = new HashSet<string>();

        foreach (var citySlug in CitySlugs)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var cityListings = await ScrapeCityAsync(citySlug, ct);
                foreach (var listing in cityListings)
                {
                    if (seen.Add(listing.ExternalId))
                        listings.Add(listing);
                }
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // city slug doesn't exist in Nederwoon's system — skip silently
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
        http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://www.nederwoon.nl/");

        var html = await http.GetStringAsync(BaseUrl + citySlug, ct);

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html), ct);

        var listings = new List<ScrapedListing>();

        foreach (var anchor in document.QuerySelectorAll("a[href]").OfType<IHtmlAnchorElement>())
        {
            try
            {
                var href = anchor.GetAttribute("href") ?? string.Empty;
                if (!TryParseListingHref(href, out var externalId, out var city)) continue;

                var title = anchor.TextContent.Trim();
                if (string.IsNullOrEmpty(title)) continue;

                // Price is a sibling element outside the anchor — walk up to card container
                var container = anchor.ParentElement ?? anchor;
                var price = ScraperHelpers.ParsePrice(container.TextContent);
                if (price <= 0) continue;

                listings.Add(new ScrapedListing(externalId, title, city, price,
                    "https://www.nederwoon.nl" + href, SourceName));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Nederwoon: skipped malformed listing");
            }
        }

        return listings;
    }

    private static bool TryParseListingHref(string href, out string externalId, out string city)
    {
        externalId = string.Empty;
        city = string.Empty;

        // /huurwoning/{city}/{numericId}/{slug}
        var parts = href.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || parts[0] != "huurwoning") return false;
        if (!long.TryParse(parts[2], out _)) return false;

        externalId = parts[2];
        city = parts[1].Replace('-', ' ');

        return true;
    }
}
