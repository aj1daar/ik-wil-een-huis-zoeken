using IWEHZ.Domain.Models;
using IWEHZ.Infrastructure.Markdown;
using IWEHZ.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace IWEHZ.Services;

public sealed class NotificationDispatcher(
    IDbContextFactory<AppDbContext> dbFactory,
    ITelegramBotClient bot,
    ILogger<NotificationDispatcher> logger)
{
    public async Task DispatchAsync(RentalListing listing, CancellationToken ct) =>
        await DispatchBatchAsync([listing], ct);

    public async Task DispatchBatchAsync(IReadOnlyList<RentalListing> listings, CancellationToken ct)
    {
        if (listings.Count == 0) return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var listingIds = listings.Select(l => l.Id).ToHashSet();

        var alreadyNotified = await db.NotificationLogs
            .AsNoTracking()
            .Where(n => listingIds.Contains(n.ListingId))
            .Select(n => new { n.UserId, n.ListingId })
            .ToListAsync(ct);

        var users = await db.Users
            .AsNoTracking()
            .Include(u => u.UserCities)
            .ThenInclude(uc => uc.City)
            .Where(u =>
                u.IsActive &&
                !u.IsPaused &&
                u.OnboardingState == OnboardingState.Completed &&
                (u.MaxBudget == null || u.MaxBudget >= listings.Min(l => l.Price)))
            .ToListAsync(ct);

        foreach (var user in users)
        {
            var matched = listings.Where(listing =>
            {
                if (alreadyNotified.Any(n => n.UserId == user.Id && n.ListingId == listing.Id))
                    return false;
                if (user.MinBudget.HasValue && listing.Price < user.MinBudget.Value)
                    return false;
                if (user.MaxBudget.HasValue && listing.Price > user.MaxBudget.Value)
                    return false;

                var cityNorm = listing.City.Trim().ToLowerInvariant();
                return user.UserCities.Any(uc =>
                    uc.City.NameNl.ToLowerInvariant() == cityNorm ||
                    uc.City.NameEn.ToLowerInvariant() == cityNorm);
            }).ToList();

            if (matched.Count == 0) continue;

            await SendAlertAsync(user, matched, ct);

            foreach (var listing in matched)
            {
                db.NotificationLogs.Add(new NotificationLog
                {
                    UserId = user.Id,
                    ListingId = listing.Id,
                    SentAt = DateTime.UtcNow,
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task DispatchPriceDropsAsync(IReadOnlyList<RentalListing> listings, CancellationToken ct)
    {
        if (listings.Count == 0) return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var users = await db.Users
            .AsNoTracking()
            .Include(u => u.UserCities)
            .ThenInclude(uc => uc.City)
            .Where(u => u.IsActive && !u.IsPaused && u.OnboardingState == OnboardingState.Completed)
            .ToListAsync(ct);

        foreach (var user in users)
        {
            var matched = listings.Where(listing =>
            {
                if (user.MinBudget.HasValue && listing.Price < user.MinBudget.Value) return false;
                if (user.MaxBudget.HasValue && listing.Price > user.MaxBudget.Value) return false;
                var cityNorm = listing.City.Trim().ToLowerInvariant();
                return user.UserCities.Any(uc =>
                    uc.City.NameNl.ToLowerInvariant() == cityNorm ||
                    uc.City.NameEn.ToLowerInvariant() == cityNorm);
            }).ToList();

            if (matched.Count == 0) continue;

            foreach (var listing in matched)
            {
                var drop = listing.PreviousPrice!.Value - listing.Price;
                var msg =
                    $"📉 *Price drop\\!*\n\n" +
                    $"*{MarkdownHelper.EscapeV2(listing.Title)}*\n" +
                    $"📍 {MarkdownHelper.EscapeV2(listing.City)}\n" +
                    $"💶 ~~€{listing.PreviousPrice.Value:N0}~~ → *€{listing.Price:N0}*/month \\(\\-€{drop:N0}\\)\n" +
                    $"🔗 [View listing]({MarkdownHelper.EscapeV2(listing.SourceUrl)})\n" +
                    $"_Source: {MarkdownHelper.EscapeV2(listing.Source)}_";

                await SendWithRetryAsync(user.TelegramChatId, user.Id, msg, ct);
            }
        }
    }

    private async Task SendAlertAsync(User user, List<RentalListing> listings, CancellationToken ct)
    {
        var message = listings.Count == 1
            ? FormatSingle(listings[0])
            : FormatBatch(listings);

        await SendWithRetryAsync(user.TelegramChatId, user.Id, message, ct);
    }

    private async Task SendWithRetryAsync(long chatId, int userId, string message, CancellationToken ct)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await bot.SendMessage(
                    chatId: chatId,
                    text: message,
                    parseMode: ParseMode.MarkdownV2,
                    cancellationToken: ct);
                return;
            }
            catch (Telegram.Bot.Exceptions.ApiRequestException ex) when (ex.ErrorCode == 429 && attempt < maxAttempts)
            {
                var retryAfter = ex.Parameters?.RetryAfter ?? attempt * 5;
                logger.LogWarning("Telegram rate limit for user {UserId}, retrying in {Delay}s", userId, retryAfter);
                await Task.Delay(TimeSpan.FromSeconds(retryAfter), ct);
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                logger.LogWarning(ex, "Send attempt {Attempt} failed for user {UserId}, retrying", attempt, userId);
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send notification to user {UserId} after {Max} attempts", userId, maxAttempts);
            }
        }
    }

    private static string FormatSingle(RentalListing listing) =>
        $"🏠 *New listing*\n\n" +
        $"*{MarkdownHelper.EscapeV2(listing.Title)}*\n" +
        $"📍 {MarkdownHelper.EscapeV2(listing.City)}\n" +
        $"💶 €{listing.Price:N0}/month\n" +
        $"🔗 [View listing]({MarkdownHelper.EscapeV2(listing.SourceUrl)})\n" +
        $"_Source: {MarkdownHelper.EscapeV2(listing.Source)}_";

    private static string FormatBatch(List<RentalListing> listings)
    {
        var lines = new System.Text.StringBuilder();
        lines.AppendLine($"🏠 *{listings.Count} new listings found\\!*\n");

        foreach (var l in listings)
        {
            lines.AppendLine(
                $"• [{MarkdownHelper.EscapeV2(l.Title)}]({MarkdownHelper.EscapeV2(l.SourceUrl)}) — " +
                $"📍 {MarkdownHelper.EscapeV2(l.City)} — " +
                $"💶 €{l.Price:N0}");
        }

        return lines.ToString().TrimEnd();
    }
}
