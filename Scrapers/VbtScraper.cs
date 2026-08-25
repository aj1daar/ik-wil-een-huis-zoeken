using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using IWEHZ.Infrastructure.Http;

namespace IWEHZ.Scrapers;

public sealed class VbtScraper : IPropertyScraper
{
    private const string BaseUrl = "https://vbtverhuurmakelaars.nl/woningen";
    private const string Source = "vbt";
    private const int MaxPages = 60;
    private readonly string? _proxyUrl;
    private readonly ILogger<VbtScraper> _logger;

    public string SourceName => Source;

    public VbtScraper(Microsoft.Extensions.Configuration.IConfiguration config, ILogger<VbtScraper> logger)
    {
        var sourceOverride = config[$"Scraper:SourceProxyUrl:{SourceName}"];
        _proxyUrl = string.IsNullOrWhiteSpace(sourceOverride) ? config["Scraper:ProxyUrl"] : sourceOverride;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScrapedListing>> ScrapeAsync(CancellationToken ct)
    {
        using var http = ScraperHttpClientFactory.Create(_proxyUrl);
        http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://vbtverhuurmakelaars.nl/");

        var listings = new List<ScrapedListing>();
        var seen = new HashSet<string>();

        // /woningen, /woningen/2, /woningen/3, ... — site 404s past the last real page.
        for (var page = 1; page <= MaxPages; page++)
        {
            var pageUrl = page == 1 ? BaseUrl : $"{BaseUrl}/{page}";

            using var response = await http.GetAsync(pageUrl, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                break;
            response.EnsureSuccessStatusCode();

            var html = await response.Content.ReadAsStringAsync(ct);
            var context = BrowsingContext.New(Configuration.Default);
            var document = await context.OpenAsync(req => req.Content(html), ct);

            var cards = document.QuerySelectorAll("a.property").OfType<IHtmlAnchorElement>().ToList();
            if (cards.Count == 0) break;

            foreach (var card in cards)
            {
                try
                {
                    if (TryParseCard(card, out var listing) && seen.Add(listing.ExternalId))
                        listings.Add(listing);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "vb&t: skipped malformed listing");
                }
            }

            if (page < MaxPages)
                await Task.Delay(TimeSpan.FromSeconds(1.5), ct);
        }

        return listings;
    }

    internal static bool TryParseCard(IElement card, out ScrapedListing listing)
    {
        listing = null!;

        var href = (card as IHtmlAnchorElement)?.GetAttribute("href") ?? string.Empty;
        var externalId = ScraperHelpers.ExtractLastUrlSegment(href);
        if (string.IsNullOrEmpty(externalId)) return false;

        var itemsDiv = card.QuerySelector("div.items");
        if (itemsDiv is null) return false;

        var city = itemsDiv.Children.OfType<IElement>().FirstOrDefault(e => e.TagName == "DIV")?.TextContent.Trim()
            ?? string.Empty;
        if (string.IsNullOrEmpty(city)) return false;

        var street = itemsDiv.QuerySelector("span.normal")?.TextContent.Trim() ?? string.Empty;

        var price = ScraperHelpers.ParsePrice(itemsDiv.QuerySelector("div.price")?.TextContent ?? string.Empty);
        if (price <= 0) return false;

        string? propertyType = null;
        foreach (var row in itemsDiv.QuerySelectorAll("table tr"))
        {
            var cells = row.QuerySelectorAll("td");
            if (cells.Length >= 2 && cells[0].TextContent.Trim() == "Soort object")
            {
                propertyType = cells[1].TextContent.Trim();
                break;
            }
        }

        var title = propertyType switch
        {
            null or "" when string.IsNullOrEmpty(street) => $"Huurwoning {city}",
            null or "" => street,
            _ when string.IsNullOrEmpty(street) => propertyType,
            _ => $"{propertyType} {street}",
        };

        var url = href.StartsWith("http", StringComparison.Ordinal) ? href : "https://vbtverhuurmakelaars.nl" + href;

        listing = new ScrapedListing(externalId, title, city, price, url, Source);
        return true;
    }
}
