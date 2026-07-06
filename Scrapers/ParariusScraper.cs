using AngleSharp;
using AngleSharp.Html.Dom;
using Microsoft.Playwright;

namespace IWEHZ.Scrapers;

public sealed class ParariusScraper : IPropertyScraper
{
    private const string Url = "https://www.pararius.nl/huurwoningen/nederland";
    private const int MaxAttempts = 3;
    private readonly string? _proxyUrl;
    private readonly ILogger<ParariusScraper> _logger;

    public string SourceName => "pararius";

    public ParariusScraper(Microsoft.Extensions.Configuration.IConfiguration config, ILogger<ParariusScraper> logger)
    {
        var sourceOverride = config[$"Scraper:SourceProxyUrl:{SourceName}"];
        _proxyUrl = sourceOverride is not null ? sourceOverride : config["Scraper:ProxyUrl"];
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScrapedListing>> ScrapeAsync(CancellationToken ct)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var listings = await TryAttemptAsync(attempt, ct);
            if (listings is not null)
                return listings;

            if (attempt < MaxAttempts)
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }

        _logger.LogWarning("Pararius: all {Max} attempts failed — Cloudflare challenge not passed", MaxAttempts);
        return [];
    }

    private async Task<IReadOnlyList<ScrapedListing>?> TryAttemptAsync(int attempt, CancellationToken ct)
    {
        using var playwright = await Playwright.CreateAsync();

        Microsoft.Playwright.Proxy? playwrightProxy = null;
        if (!string.IsNullOrWhiteSpace(_proxyUrl) && Uri.TryCreate(_proxyUrl, UriKind.Absolute, out var proxyUri))
        {
            var server = $"{proxyUri.Scheme}://{proxyUri.Host}:{proxyUri.Port}";
            playwrightProxy = new Microsoft.Playwright.Proxy { Server = server };

            if (!string.IsNullOrEmpty(proxyUri.UserInfo))
            {
                var colonIdx = proxyUri.UserInfo.IndexOf(':');
                playwrightProxy.Username = colonIdx >= 0
                    ? Uri.UnescapeDataString(proxyUri.UserInfo[..colonIdx])
                    : Uri.UnescapeDataString(proxyUri.UserInfo);
                playwrightProxy.Password = colonIdx >= 0
                    ? Uri.UnescapeDataString(proxyUri.UserInfo[(colonIdx + 1)..])
                    : string.Empty;
            }
        }

        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            Proxy = playwrightProxy,
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
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000,
        });

        try
        {
            await page.WaitForSelectorAsync("section.listing-search-item", new PageWaitForSelectorOptions
            {
                Timeout = 45_000,
            });
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Pararius: attempt {Attempt}/{Max} — selector not found, retrying with new IP",
                attempt, MaxAttempts);
            return null;
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
