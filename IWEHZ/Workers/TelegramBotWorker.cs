using IWEHZ.Bot.Handlers;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace IWEHZ.Workers;

public sealed class TelegramBotWorker(
    ITelegramBotClient bot,
    MessageHandler messageHandler,
    ILogger<TelegramBotWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message],
            DropPendingUpdates = true,
        };

        bot.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken);

        logger.LogInformation("Telegram bot started receiving");

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleUpdateAsync(ITelegramBotClient client, Update update, CancellationToken ct)
    {
        if (update.Message is not { } message) return;

        try
        {
            await messageHandler.HandleAsync(client, message, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error processing message from {ChatId}", message.Chat.Id);
        }
    }

    private Task HandleErrorAsync(ITelegramBotClient client, Exception exception, HandleErrorSource source, CancellationToken ct)
    {
        var msg = exception switch
        {
            ApiRequestException api => $"Telegram API error [{api.ErrorCode}]: {api.Message}",
            _ => exception.ToString(),
        };

        logger.LogError("Telegram polling error ({Source}): {Message}", source, msg);
        return Task.CompletedTask;
    }
}
