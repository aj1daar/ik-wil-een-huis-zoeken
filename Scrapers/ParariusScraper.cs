using AngleSharp;
using AngleSharp.Html.Dom;
using Microsoft.Playwright;

namespace IWEHZ.Scrapers;

public sealed class ParariusScraper : IPropertyScraper
{
    private const string Url = "https://www.pararius.nl/huurwoningen/nederland";
    private readonly ILogger<ParariusScraper> _logger;

    public string SourceName => "pararius";

    public ParariusScraper(ILogger<ParariusScraper> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScrapedListing>> ScrapeAsync(CancellationToken ct)
    {
        using var playwright = await Playwright.CreateAsync();

        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Args =
            [
                "--disable-blink-features=AutomationControlled",
                "--no-sandbox",
                "--disable-dev-shm-usage",
                "--disable-gpu",
            ],
        });

        var page = await browser.NewPageAsync(new BrowserNewPageOptions
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36",
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["Accept-Language"] = "nl-NL,nl;q=0.9,en-US;q=0.8,en;q=0.7",
            },
        });

        await page.AddInitScriptAsync("Object.defineProperty(navigator, 'webdriver', {get: () => undefined})");

        await page.GotoAsync(Url, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 60_000,
        });

        try
        {
            await page.WaitForSelectorAsync("section.listing-search-item", new PageWaitForSelectorOptions
            {
                Timeout = 15_000,
            });
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Pararius: listing selector not found — page may have changed or challenge not passed");
            return [];
        }

        var html = await page.ContentAsync();
        await page.CloseAsync();

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
