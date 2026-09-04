using System.Text.RegularExpressions;
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

                // Raw attribute, not the resolved .Href — the parsed document has no base
                // address, so .Href silently resolved relative links to http://localhost/...
                var rawHref = anchor.GetAttribute("href") ?? string.Empty;
                var externalId = ScraperHelpers.ExtractLastUrlSegment(rawHref);
                if (string.IsNullOrEmpty(externalId)) continue;

                var url = rawHref.StartsWith("http", StringComparison.Ordinal)
                    ? rawHref
                    : "https://www.pararius.nl" + rawHref;

                var title = anchor.TextContent.Trim();

                // Sub-title is "{postcode} {City} ({neighbourhood})", e.g.
                // "1102 RR Amsterdam (Amsterdamse Poort e.o.)" — strip both, or matching
                // against a bare city name (e.g. from a user's city list) never hits.
                var city = ExtractCity(article.QuerySelector(".listing-search-item__sub-title")?.TextContent ?? string.Empty);
                var price = ScraperHelpers.ParsePrice(
                    article.QuerySelector(".listing-search-item__price")?.TextContent ?? string.Empty);
                if (price <= 0) continue;

                listings.Add(new ScrapedListing(externalId, title, city, price, url, SourceName));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Pararius: skipped malformed listing element");
            }
        }

        return listings;
    }

    private static readonly Regex SubTitlePattern =
        new(@"^\d{4}\s*[A-Za-z]{2}\s+(?<city>.+?)(?:\s*\(.*\))?$", RegexOptions.Compiled);

    internal static string ExtractCity(string subTitle)
    {
        var trimmed = subTitle.Trim();
        var match = SubTitlePattern.Match(trimmed);
        return match.Success ? match.Groups["city"].Value.Trim() : trimmed;
    }
}
