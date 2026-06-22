using IWEHZ.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IWEHZ.Workers;

public sealed class CleanupWorker(
    IDbContextFactory<AppDbContext> dbFactory,
    IConfiguration config,
    ILogger<CleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var retentionDays = config.GetValue("Cleanup:RetentionDays", 30);
                var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

                await using var db = await dbFactory.CreateDbContextAsync(stoppingToken);
                var deleted = await db.RentalListings
                    .Where(l => l.ScrapedAt < cutoff)
                    .ExecuteDeleteAsync(stoppingToken);

                if (deleted > 0)
                    logger.LogInformation("Cleanup: removed {Count} listings older than {Days} days", deleted, retentionDays);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Cleanup worker failed");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}
