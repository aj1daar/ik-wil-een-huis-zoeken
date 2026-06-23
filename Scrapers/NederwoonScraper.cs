using System.Net;
using AngleSharp;
using AngleSharp.Html.Dom;
using IWEHZ.Domain.Models;
using IWEHZ.Infrastructure.Http;
using IWEHZ.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IWEHZ.Scrapers;

public sealed class NederwoonScraper : IPropertyScraper
{
    private const string BaseUrl = "https://www.nederwoon.nl/huurwoningen/";
    private readonly string? _proxyUrl;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<NederwoonScraper> _logger;

    public string SourceName => "nederwoon";

    public NederwoonScraper(Microsoft.Extensions.Configuration.IConfiguration config, IDbContextFactory<AppDbContext> dbFactory, ILogger<NederwoonScraper> logger)
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
        http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://www.nederwoon.nl/");

        var html = await http.GetStringAsync(BaseUrl + citySlug, ct);

        var context = BrowsingContext.New(Configuration.Default);
        var document = await context.OpenAsync(req => req.Content(html), ct);

        var listings = new List<ScrapedListing>();

        foreach (var card in document.QuerySelectorAll("div.location"))
        {
            try
            {
                var anchor = card.QuerySelector("a.see-page-button[href]") as IHtmlAnchorElement;
                if (anchor is null) continue;

                var href = anchor.GetAttribute("href") ?? string.Empty;
                if (!TryParseListingHref(href, out var externalId, out var city)) continue;

                var title = anchor.TextContent.Trim();
                if (string.IsNullOrEmpty(title)) continue;

                var price = ScraperHelpers.ParsePrice(card.QuerySelector(".heading-md")?.TextContent ?? string.Empty);
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

        // /huurwoning/{city}/{numericId} or /huurwoning/{city}/{numericId}/{slug}
        var parts = href.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || parts[0] != "huurwoning") return false;
        if (!long.TryParse(parts[2], out _)) return false;

        externalId = parts[2];
        city = parts[1].Replace('-', ' ');

        return true;
    }
}
