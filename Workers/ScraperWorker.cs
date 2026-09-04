using System.Security.Cryptography;
using System.Text;
using IWEHZ.Domain.Models;
using IWEHZ.Infrastructure.Http;
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
    ScraperFetcher fetcher,
    IConfiguration config,
    ILogger<ScraperWorker> logger) : BackgroundService
{
    private readonly Dictionary<string, DateTime> _lastRun = new();
    private (int Used, int Limit, DateTime CheckedAt)? _credits;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minDelay = config.GetValue("Scraper:IntervalMinSeconds", 60);
        var maxDelay = config.GetValue("Scraper:IntervalMaxSeconds", 120);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (await IsCreditBudgetExhaustedAsync(stoppingToken))
            {
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
                continue;
            }

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
                catch (ScraperApiAccountException ex)
                {
                    logger.LogError("ScraperAPI account failure (HTTP {Code}) — skipping remaining sources this cycle", ex.StatusCode);
                    var detail = ex.StatusCode switch
                    {
                        401 => "bad API key",
                        403 => "out of credits",
                        429 => "concurrency limit hit",
                        _ => $"HTTP {ex.StatusCode}",
                    };
                    await adminNotifier.NotifyAsync(
                        "scraperapi:account",
                        $"⚠️ ScraperAPI problem: {detail} (HTTP {ex.StatusCode}). All scrapers paused until resolved.\n{DateTime.UtcNow:u}",
                        cooldown: TimeSpan.FromHours(1));
                    break;
                }
                catch (AllProxyAttemptsBlockedException ex)
                {
                    logger.LogWarning("Scraper {Source} blocked on all {Attempts} attempts ({Reason})", ex.SourceName, ex.Attempts, ex.Reason);
                    var reasonSuffix = ex.Reason is null ? "" : $" (last: {ex.Reason})";
                    await adminNotifier.NotifyAsync(
                        $"{scraper.SourceName}:proxy-blocked",
                        $"🚫 [{scraper.SourceName}] blocked on all {ex.Attempts} attempts{reasonSuffix}\n{DateTime.UtcNow:u}",
                        cooldown: TimeSpan.FromHours(4));
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

    private static string ComputeFingerprint(string city, decimal price, string title)
    {
        var streetPart = title.Split(',')[0].Trim().ToLowerInvariant();
        if (streetPart.Length > 40) streetPart = streetPart[..40];
        var priceBucket = (int)(price / 50) * 50;
        var input = $"{city.ToLowerInvariant()}|{priceBucket}|{streetPart}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16];
    }

    // Free-tier guard: stop all scraping once ScraperAPI credit usage crosses the
    // configured ratio, so a busy month can't spill past the 1,000-credit plan.
    // The /account check is free and cached for an hour.
    private async Task<bool> IsCreditBudgetExhaustedAsync(CancellationToken ct)
    {
        if (!fetcher.UsesScraperApi) return false;

        var stopRatio = config.GetValue("Scraper:ScraperApiCreditStopRatio", 0.95);
        if (stopRatio <= 0) return false;

        if (_credits is null || DateTime.UtcNow - _credits.Value.CheckedAt > TimeSpan.FromHours(1))
        {
            var usage = await fetcher.GetCreditUsageAsync(ct);
            if (usage is null) return false; // check failed — don't block scraping
            _credits = (usage.Value.Used, usage.Value.Limit, DateTime.UtcNow);
        }

        var (used, limit, _) = _credits.Value;
        if (limit <= 0 || used < limit * stopRatio) return false;

        logger.LogWarning("ScraperAPI credit budget reached: {Used}/{Limit} — pausing all scrapers", used, limit);
        await adminNotifier.NotifyAsync(
            "scraperapi:budget",
            $"🧮 ScraperAPI credits {used}/{limit} (≥{stopRatio:P0}). Scraping paused until the monthly reset.\n{DateTime.UtcNow:u}",
            cooldown: TimeSpan.FromHours(12), ct: ct);
        return true;
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
            catch (HttpRequestException ex) when (IsScraperApiAccountFailure(ex))
            {
                throw new ScraperApiAccountException((int)ex.StatusCode!.Value);
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                var code = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : 0;
                logger.LogWarning("Scraper {Source} attempt {Attempt}/{Max} got HTTP {Code}, retrying in {Delay}s",
                    scraper.SourceName, attempt, maxAttempts, code, attempt * 3);
                await Task.Delay(TimeSpan.FromSeconds(attempt * 3), ct);
            }
            catch (HttpRequestException ex)
            {
                var reason = ex.StatusCode.HasValue ? $"HTTP {(int)ex.StatusCode.Value}" : ex.Message;
                throw new AllProxyAttemptsBlockedException(scraper.SourceName, maxAttempts, reason);
            }
            catch (TaskCanceledException) when (attempt < maxAttempts && !ct.IsCancellationRequested)
            {
                logger.LogWarning("Scraper {Source} attempt {Attempt}/{Max} timed out, retrying in {Delay}s",
                    scraper.SourceName, attempt, maxAttempts, attempt * 10);
                await Task.Delay(TimeSpan.FromSeconds(attempt * 10), ct);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new AllProxyAttemptsBlockedException(scraper.SourceName, maxAttempts, "timeout");
            }
        }
    }

    // 401 bad key, 403 out of credits, 429 concurrency limit — all account-level, not a site block.
    private static bool IsScraperApiAccountFailure(HttpRequestException ex) =>
        ex.StatusCode is System.Net.HttpStatusCode.Unauthorized
            or System.Net.HttpStatusCode.Forbidden
            or System.Net.HttpStatusCode.TooManyRequests;

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

        // Mark listings from this source that are no longer in the current scrape as unavailable
        var currentExternalIds = listings.Select(l => l.ExternalId).ToList();
        var delistedCount = await db.RentalListings
            .Where(l => l.Source == scraper.SourceName && l.IsAvailable && !currentExternalIds.Contains(l.ExternalId))
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.IsAvailable, false), ct);

        if (delistedCount > 0)
            logger.LogInformation("{Source} marked {Count} listings as unavailable", scraper.SourceName, delistedCount);

        var newEntities = new List<RentalListing>();
        var priceDropEntities = new List<RentalListing>();

        foreach (var scraped in listings)
        {
            var existing = await db.RentalListings
                .FirstOrDefaultAsync(l => l.ExternalId == scraped.ExternalId && l.Source == scraped.Source, ct);

            if (existing is not null)
            {
                var needsUpdate = scraped.Price < existing.Price;
                if (!existing.IsAvailable || needsUpdate)
                {
                    await db.RentalListings
                        .Where(l => l.Id == existing.Id)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(l => l.IsAvailable, true)
                            .SetProperty(l => l.PreviousPrice, needsUpdate ? existing.Price : existing.PreviousPrice)
                            .SetProperty(l => l.Price, needsUpdate ? scraped.Price : existing.Price), ct);

                    if (needsUpdate)
                    {
                        existing.PreviousPrice = existing.Price;
                        existing.Price = scraped.Price;
                        priceDropEntities.Add(existing);
                    }
                }
                continue;
            }

            var entity = new RentalListing
            {
                ExternalId = scraped.ExternalId,
                Source = scraped.Source,
                Title = scraped.Title,
                City = scraped.City,
                Price = scraped.Price,
                ContentFingerprint = ComputeFingerprint(scraped.City, scraped.Price, scraped.Title),
                SourceUrl = scraped.SourceUrl,
                ScrapedAt = DateTime.UtcNow,
            };

            db.RentalListings.Add(entity);
            await db.SaveChangesAsync(ct);
            newEntities.Add(entity);
        }

        if (newEntities.Count > 0)
            await dispatcher.DispatchBatchAsync(newEntities, ct);

        if (priceDropEntities.Count > 0)
            await dispatcher.DispatchPriceDropsAsync(priceDropEntities, ct);
    }
}
