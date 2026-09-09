using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using IWEHZ.Domain.Models;
using IWEHZ.Infrastructure.Http;
using IWEHZ.Scrapers;

namespace IWEHZ.Services;

/// <summary>
/// Second-pass check for listings that look new: opens the listing's own page and looks for a
/// "Verhuurd" / "Onder optie" / "Gereserveerd" style status. Search-result cards lag behind —
/// a home can be let hours before it drops off the overview page — so this is what stops a
/// dead listing from being pushed to users.
///
/// Fetches go out directly, not through ScraperAPI, so the check costs no credits — the
/// volume is small (only listings that are new this cycle, capped by
/// <c>Scraper:AvailabilityCheck:MaxPerCycle</c>) and spaced out. Sources that sit behind an
/// anti-bot wall answer a direct request with a challenge page instead of the listing, so
/// they are listed in <c>Scraper:AvailabilityCheck:SkipSources</c> and verified by their card
/// text alone; set <c>Scraper:AvailabilityCheck:UseScraperApi</c> to route through the proxy
/// instead, at one credit per listing.
///
/// It fails open: a fetch error, a challenge page or an unparseable page means the listing is
/// treated as available, because a missed alert is worse than a stale one.
/// </summary>
public sealed partial class ListingAvailabilityVerifier(
    ScraperFetcher fetcher,
    IConfiguration config,
    ILogger<ListingAvailabilityVerifier> logger)
{
    private static readonly TimeSpan RequestSpacing = TimeSpan.FromSeconds(2);

    public bool Enabled => config.GetValue("Scraper:AvailabilityCheck:Enabled", true);

    private int MaxPerCycle => config.GetValue("Scraper:AvailabilityCheck:MaxPerCycle", 25);

    private bool UseScraperApi => config.GetValue("Scraper:AvailabilityCheck:UseScraperApi", false);

    private HashSet<string> SkipSources =>
        config.GetSection("Scraper:AvailabilityCheck:SkipSources")
            .Get<string[]>()?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];

    /// <summary>Returns the subset of <paramref name="listings"/> whose own page says it is already let.</summary>
    public async Task<IReadOnlyList<RentalListing>> FindRentedAsync(
        IReadOnlyList<RentalListing> listings, CancellationToken ct)
    {
        if (!Enabled || listings.Count == 0) return [];

        var budget = MaxPerCycle;
        if (budget <= 0) return [];

        var skip = SkipSources;
        var useProxy = UseScraperApi;
        var rented = new List<RentalListing>();
        var checkedAny = false;

        foreach (var listing in listings)
        {
            if (ct.IsCancellationRequested) break;
            if (skip.Contains(listing.Source)) continue;
            if (budget-- <= 0)
            {
                logger.LogInformation("Availability check budget of {Max} reached — remaining listings not verified", MaxPerCycle);
                break;
            }

            try
            {
                // Direct requests come from our own IP, so space them out — a burst on one
                // site is what gets an IP blocked.
                if (checkedAny) await Task.Delay(RequestSpacing, ct);
                checkedAny = true;

                using var http = useProxy
                    ? fetcher.CreateClient(listing.Source)
                    : ScraperHttpClientFactory.Create();
                if (!useProxy) http.Timeout = TimeSpan.FromSeconds(20);

                var html = await http.GetStringAsync(listing.SourceUrl, ct);

                if (LooksRented(html, out var marker))
                {
                    logger.LogInformation("Listing {Source}/{ExternalId} already rented (\"{Marker}\") — suppressing alert",
                        listing.Source, listing.ExternalId, marker);
                    rented.Add(listing);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Fail open — an unreachable or challenged detail page must not swallow a real listing.
                logger.LogDebug(ex, "Availability check failed for {Url}", listing.SourceUrl);
            }
        }

        return rented;
    }

    // Only status-ish nodes are scanned, never the whole page: sidebars and filter menus on
    // these sites carry permanent "Verhuurd aanbod" links that would flag every listing.
    [GeneratedRegex("status|label|badge|sticker|ribbon|availab|beschikbaar|sold|rented",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StatusHintRegex();

    internal static bool LooksRented(string html, out string? marker)
    {
        marker = null;
        if (string.IsNullOrWhiteSpace(html)) return false;

        var document = new HtmlParser().ParseDocument(html);

        if (RentedStatusDetector.TryMatch(document.Title, out marker)) return true;

        foreach (var element in document.All)
        {
            // A wrapper with many children is a section, not a status label; its text would
            // pull in unrelated page copy.
            if (element.ChildElementCount > 3) continue;

            var isHeading = element.TagName is "H1" or "H2";
            if (!isHeading && !StatusHintRegex().IsMatch($"{element.ClassName} {element.Id}")) continue;

            var text = element.TextContent;
            if (string.IsNullOrWhiteSpace(text) || text.Length > 200) continue;

            if (RentedStatusDetector.TryMatch(text, out marker)) return true;
        }

        marker = null;
        return false;
    }
}
