using System.Collections.Concurrent;
using Telegram.Bot;

namespace IWEHZ.Services;

public sealed class AdminNotifier(
    ITelegramBotClient bot,
    IConfiguration config,
    ILogger<AdminNotifier> logger)
{
    private readonly long _adminChatId = config.GetValue<long>("Telegram:AdminChatId");
    private readonly ConcurrentDictionary<string, DateTime> _lastSent = new();

    public async Task NotifyAsync(string throttleKey, string text, TimeSpan? cooldown = null, CancellationToken ct = default)
    {
        var limit = cooldown ?? TimeSpan.FromHours(1);
        var now = DateTime.UtcNow;

        if (_lastSent.TryGetValue(throttleKey, out var last) && now - last < limit)
            return;

        _lastSent[throttleKey] = now;

        try
        {
            await bot.SendMessage(_adminChatId, text, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send admin notification for key {Key}", throttleKey);
        }
    }
}
