using System.Net;
using IWEHZ.Domain.Models;
using IWEHZ.Infrastructure.Persistence;
using IWEHZ.Scrapers;
using IWEHZ.Services;
using Microsoft.EntityFrameworkCore;

namespace IWEHZ.Workers;

public sealed class ScraperWorker(
    IEnumerable<IPropertyScraper> scrapers,
    IDbContextFactory<AppDbContext> dbFactory,
    NotificationDispatcher dispatcher,
    AdminNotifier adminNotifier,
    IConfiguration config,
    ILogger<ScraperWorker> logger) : BackgroundService
{
    private static readonly HashSet<HttpStatusCode> RetryableCodes =
    [
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout,
    ];

    private readonly Dictionary<string, DateTime> _lastRun = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minDelay = config.GetValue("Scraper:IntervalMinSeconds", 60);
        var maxDelay = config.GetValue("Scraper:IntervalMaxSeconds", 120);

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var scraper in scrapers)
            {
                if (stoppingToken.IsCancellationRequested) return;
                if (!IsDue(scraper.SourceName)) continue;

                try
                {
                    await RunWithRetryAsync(scraper, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
                {
                    logger.LogWarning("Scraper {Source} blocked with 403", scraper.SourceName);
                    await adminNotifier.NotifyAsync(
                        $"{scraper.SourceName}:403",
                        $"🚫 [{scraper.SourceName}] blocked — 403 Forbidden\n{DateTime.UtcNow:u}");
                }
                catch (HttpRequestException ex)
                {
                    var code = ex.StatusCode.HasValue ? (int)ex.StatusCode : 0;
                    logger.LogWarning(ex, "Scraper {Source} HTTP error {Code}", scraper.SourceName, code);
                    await adminNotifier.NotifyAsync(
                        $"{scraper.SourceName}:http:{code}",
                        $"⚠️ [{scraper.SourceName}] HTTP {code}\n{DateTime.UtcNow:u}");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unhandled error in scraper {Source}", scraper.SourceName);
                    await adminNotifier.NotifyAsync(
                        $"{scraper.SourceName}:crash",
                        $"❌ [{scraper.SourceName}] crashed: {ex.GetType().Name}: {ex.Message[..Math.Min(200, ex.Message.Length)]}\n{DateTime.UtcNow:u}");
                }
                finally
                {
                    _lastRun[scraper.SourceName] = DateTime.UtcNow;
                }

                if (!stoppingToken.IsCancellationRequested)
                    await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(3, 9)), stoppingToken);
            }

            var delay = TimeSpan.FromSeconds(Random.Shared.Next(minDelay, maxDelay + 1));
            await Task.Delay(delay, stoppingToken);
        }
    }

    private bool IsDue(string sourceName)
    {
        var seconds = config.GetValue<int>($"Scraper:SourceIntervalSeconds:{sourceName}", 0);
        if (seconds == 0) return true;
        if (!_lastRun.TryGetValue(sourceName, out var last)) return true;
        return DateTime.UtcNow - last >= TimeSpan.FromSeconds(seconds);
    }

    private async Task RunWithRetryAsync(IPropertyScraper scraper, CancellationToken ct)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await RunScraperAsync(scraper, ct);
                return;
            }
            catch (HttpRequestException ex) when (
                attempt < maxAttempts &&
                ex.StatusCode.HasValue &&
                RetryableCodes.Contains(ex.StatusCode.Value))
            {
                logger.LogWarning("Scraper {Source} attempt {Attempt}/{Max} got HTTP {Code}, retrying in {Delay}s",
                    scraper.SourceName, attempt, maxAttempts, (int)ex.StatusCode.Value, attempt * 3);
                await Task.Delay(TimeSpan.FromSeconds(attempt * 3), ct);
            }
        }
    }

    private async Task RunScraperAsync(IPropertyScraper scraper, CancellationToken ct)
    {
        logger.LogInformation("Scraping {Source}", scraper.SourceName);

        var listings = await scraper.ScrapeAsync(ct);

        if (listings.Count == 0)
        {
            logger.LogInformation("{Source} returned 0 listings", scraper.SourceName);
            await adminNotifier.NotifyAsync(
                $"{scraper.SourceName}:zero",
                $"⚠️ [{scraper.SourceName}] returned 0 listings — site structure may have changed\n{DateTime.UtcNow:u}",
                cooldown: TimeSpan.FromHours(4));
            return;
        }

        logger.LogInformation("{Source} returned {Count} listings", scraper.SourceName, listings.Count);

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var newEntities = new List<RentalListing>();

        foreach (var scraped in listings)
        {
            var exists = await db.RentalListings
                .AsNoTracking()
                .AnyAsync(l => l.ExternalId == scraped.ExternalId && l.Source == scraped.Source, ct);

            if (exists) continue;

            var entity = new RentalListing
            {
                ExternalId = scraped.ExternalId,
                Source = scraped.Source,
                Title = scraped.Title,
                City = scraped.City,
                Price = scraped.Price,
                SourceUrl = scraped.SourceUrl,
                ScrapedAt = DateTime.UtcNow,
            };

            db.RentalListings.Add(entity);
            await db.SaveChangesAsync(ct);
            newEntities.Add(entity);
        }

        if (newEntities.Count > 0)
            await dispatcher.DispatchBatchAsync(newEntities, ct);
    }
}
