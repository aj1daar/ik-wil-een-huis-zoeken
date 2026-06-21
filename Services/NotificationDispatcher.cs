using IWEHZ.Domain.Models;
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
    public async Task DispatchAsync(RentalListing listing, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var alreadyNotified = await db.NotificationLogs
            .AsNoTracking()
            .Where(n => n.ListingId == listing.Id)
            .Select(n => n.UserId)
            .ToHashSetAsync(ct);

        var matchedUsers = await db.Users
            .AsNoTracking()
            .Include(u => u.UserCities)
            .ThenInclude(uc => uc.City)
            .Where(u =>
                u.IsActive &&
                u.OnboardingState == OnboardingState.Completed &&
                !alreadyNotified.Contains(u.Id) &&
                (u.MaxBudget == null || listing.Price <= u.MaxBudget))
            .ToListAsync(ct);

        var listingCityNorm = listing.City.Trim().ToLowerInvariant();

        foreach (var user in matchedUsers)
        {
            var cityMatch = user.UserCities.Any(uc =>
                uc.City.NameNl.ToLowerInvariant() == listingCityNorm ||
                uc.City.NameEn.ToLowerInvariant() == listingCityNorm);

            if (!cityMatch) continue;

            await SendAlertAsync(user, listing, ct);

            db.NotificationLogs.Add(new NotificationLog
            {
                UserId = user.Id,
                ListingId = listing.Id,
                SentAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task SendAlertAsync(User user, RentalListing listing, CancellationToken ct)
    {
        var message =
            $"🏠 *New rental listing*\n\n" +
            $"*{EscapeMd(listing.Title)}*\n" +
            $"📍 {EscapeMd(listing.City)}\n" +
            $"💶 €{listing.Price:N0} / month\n" +
            $"🔗 [View listing]({listing.SourceUrl})\n" +
            $"_Source: {EscapeMd(listing.Source)}_";

        try
        {
            await bot.SendMessage(
                chatId: user.TelegramChatId,
                text: message,
                parseMode: ParseMode.MarkdownV2,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send notification to user {UserId}", user.Id);
        }
    }

    private static string EscapeMd(string text) =>
        text.Replace("_", "\\_").Replace("*", "\\*").Replace("[", "\\[").Replace("`", "\\`").Replace(".", "\\.").Replace("!", "\\!").Replace("-", "\\-").Replace("(", "\\(").Replace(")", "\\)");
}
