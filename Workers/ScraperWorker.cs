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
    IConfiguration config,
    ILogger<ScraperWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minDelay = config.GetValue("Scraper:IntervalMinSeconds", 60);
        var maxDelay = config.GetValue("Scraper:IntervalMaxSeconds", 120);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(Random.Shared.Next(minDelay, maxDelay + 1));

            await Task.Delay(delay, stoppingToken);

            foreach (var scraper in scrapers)
            {
                try
                {
                    await RunScraperAsync(scraper, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unhandled error in scraper {Source}", scraper.SourceName);
                }

                if (!stoppingToken.IsCancellationRequested)
                    await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(3, 9)), stoppingToken);
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
            return;
        }

        logger.LogInformation("{Source} returned {Count} listings", scraper.SourceName, listings.Count);

        await using var db = await dbFactory.CreateDbContextAsync(ct);

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

            await dispatcher.DispatchAsync(entity, ct);
        }
    }
}
