using IWEHZ.Infrastructure.Markdown;
using IWEHZ.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;

namespace IWEHZ.Services;

/// <summary>
/// Withdraws alerts for homes that have since been let. Each alert is one message with its
/// Telegram message id stored on the notification log, so the exact message can be pulled
/// back out of the user's chat instead of leaving a dead lead in the history.
///
/// Telegram only lets a bot delete its own message for 48 hours. Past that the delete is
/// refused, so the message is edited into a struck-through "verhuurd" note instead — editing
/// has no such window. Either way the log row is marked retracted so it is never touched twice.
/// </summary>
public sealed class ListingRetractor(
    IDbContextFactory<AppDbContext> dbFactory,
    ITelegramBotClient bot,
    IConfiguration config,
    ILogger<ListingRetractor> logger)
{
    private static readonly TimeSpan RequestSpacing = TimeSpan.FromMilliseconds(350);

    private bool Enabled => config.GetValue("Telegram:RetractRentedAlerts", true);

    /// <summary>Deletes (or strikes through) every standing alert for the given listings. Returns how many were withdrawn.</summary>
    public async Task<int> RetractAsync(IReadOnlyCollection<int> listingIds, CancellationToken ct)
    {
        if (!Enabled || listingIds.Count == 0) return 0;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var pending = await db.NotificationLogs
            .Where(n => listingIds.Contains(n.ListingId) && n.MessageId != null && n.RetractedAt == null)
            .Select(n => new PendingRetraction(
                n.Id, n.User.TelegramChatId, n.MessageId!.Value, n.Listing.Title, n.Listing.City))
            .ToListAsync(ct);

        if (pending.Count == 0) return 0;

        var retractedIds = new List<int>();

        for (var i = 0; i < pending.Count; i++)
        {
            if (ct.IsCancellationRequested) break;
            if (i > 0) await Task.Delay(RequestSpacing, ct);

            if (await WithdrawAsync(pending[i], ct))
                retractedIds.Add(pending[i].LogId);
        }

        if (retractedIds.Count > 0)
        {
            await db.NotificationLogs
                .Where(n => retractedIds.Contains(n.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.RetractedAt, DateTime.UtcNow), ct);

            logger.LogInformation("Retracted {Count} alerts for listings that are no longer available", retractedIds.Count);
        }

        return retractedIds.Count;
    }

    private async Task<bool> WithdrawAsync(PendingRetraction item, CancellationToken ct)
    {
        try
        {
            await bot.DeleteMessage(item.ChatId, item.MessageId, ct);
            return true;
        }
        catch (ApiRequestException ex)
        {
            // "message to delete not found" — the user deleted it themselves; nothing left to do,
            // but the row is settled either way.
            if (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
                return true;

            logger.LogDebug(ex, "Delete refused for message {MessageId}, falling back to an edit", item.MessageId);
            return await MarkAsRentedAsync(item, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not retract message {MessageId} in chat {ChatId}", item.MessageId, item.ChatId);
            return false;
        }
    }

    private async Task<bool> MarkAsRentedAsync(PendingRetraction item, CancellationToken ct)
    {
        var text =
            $"🚫 *Verhuurd — no longer available*\n\n" +
            $"~{MarkdownHelper.EscapeV2(item.Title)}~\n" +
            $"📍 {MarkdownHelper.EscapeV2(item.City)}";

        try
        {
            await bot.EditMessageText(
                chatId: item.ChatId,
                messageId: item.MessageId,
                text: text,
                parseMode: ParseMode.MarkdownV2,
                cancellationToken: ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not mark message {MessageId} as rented in chat {ChatId}", item.MessageId, item.ChatId);
            return false;
        }
    }

    private sealed record PendingRetraction(int LogId, long ChatId, int MessageId, string Title, string City);
}
