using System.Net;
using AngleSharp;
using AngleSharp.Html.Dom;
using IWEHZ.Domain.Models;
using IWEHZ.Infrastructure.Http;
using IWEHZ.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IWEHZ.Scrapers;

public sealed class WonenScraper123 : IPropertyScraper
{
    private const string BaseUrl = "https://www.123wonen.nl/huurwoningen/in/";
    private readonly string? _proxyUrl;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<WonenScraper123> _logger;

    public string SourceName => "123wonen";

    public WonenScraper123(Microsoft.Extensions.Configuration.IConfiguration config, IDbContextFactory<AppDbContext> dbFactory, ILogger<WonenScraper123> logger)
    {
        var sourceOverride = config[$"Scraper:SourceProxyUrl:{SourceName}"];
        _proxyUrl = sourceOverride is not null ? sourceOverride : config["Scraper:ProxyUrl"];
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ScrapedListing>> ScrapeAsync(CancellationToken ct)
    {
        var citySlugs = await GetActiveCitySlugsAsync(ct);
        if (citySlugs.Count == 0) return [];

        var listings = new List<ScrapedListing>();
        var seen = new HashSet<string>();

        foreach (var citySlug in citySlugs)
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
                // city slug not present on 123wonen — skip
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        return listings;
    }

    private async Task<List<string>> GetActiveCitySlugsAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var names = await db.UserCities
            .Where(uc => uc.User.IsActive && uc.User.OnboardingState == OnboardingState.Completed)
            .Select(uc => uc.City.NameNl)
            .Distinct()
            .ToListAsync(ct);
        return names.Select(n => n.ToLowerInvariant().Replace(' ', '-')).ToList();
    }

    private async Task<List<ScrapedListing>> ScrapeCityAsync(string citySlug, CancellationToken ct)
    {
        using var http = ScraperHttpClientFactory.Create(_proxyUrl);
        http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://www.123wonen.nl/");

        var html = await http.GetStringAsync(BaseUrl + citySlug + "/", ct);

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html), ct);

        var listings = new List<ScrapedListing>();

        foreach (var card in document.QuerySelectorAll("div.pandlist-container"))
        {
            try
            {
                var anchor = card.QuerySelector("a.textlink-design[href]") as IHtmlAnchorElement;
                if (anchor is null) continue;

                var href = anchor.GetAttribute("href") ?? string.Empty;
                if (!TryParseListingHref(href, out var externalId, out var city)) continue;

                var price = ScraperHelpers.ParsePrice(card.QuerySelector(".pand-price")?.TextContent ?? string.Empty);
                if (price <= 0) continue;

                var title = card.QuerySelector(".pand-title")?.TextContent.Trim()
                    ?? card.QuerySelector(".pand-slogan span")?.TextContent.Trim()
                    ?? $"Huurwoning {city}";

                var fullUrl = href.StartsWith("http") ? href : "https://www.123wonen.nl" + href;
                listings.Add(new ScrapedListing(externalId, title, city, price, fullUrl, SourceName));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "123wonen: skipped malformed listing");
            }
        }

        return listings;
    }

    private static bool TryParseListingHref(string href, out string externalId, out string city)
    {
        externalId = string.Empty;
        city = string.Empty;

        // /huur/{city}/{type}/{street}-{id1}-{id2}
        var path = href.StartsWith("http") && Uri.TryCreate(href, UriKind.Absolute, out var u)
            ? u.AbsolutePath : href;

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || parts[0] != "huur") return false;

        // Last segment: "van+boshuizenstraat-968-37" → find last two numeric dash-segments
        var segments = parts[3].Split('-');
        if (segments.Length < 3) return false;

        var id2 = segments[^1];
        var id1 = segments[^2];
        if (!long.TryParse(id1, out _) || !long.TryParse(id2, out _)) return false;

        externalId = $"{id1}-{id2}";
        city = parts[1].Replace('-', ' ');

        return true;
    }
}
