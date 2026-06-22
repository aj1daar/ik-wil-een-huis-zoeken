using IWEHZ.Bot.Handlers;
using IWEHZ.Services;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace IWEHZ.Workers;

public sealed class TelegramBotWorker(
    ITelegramBotClient bot,
    MessageHandler messageHandler,
    UserService userService,
    ILogger<TelegramBotWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [UpdateType.Message, UpdateType.CallbackQuery],
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
        try
        {
            if (update.Message is { } message)
            {
                await messageHandler.HandleAsync(client, message, ct);
            }
            else if (update.CallbackQuery is { } query)
            {
                await HandleCallbackQueryAsync(client, query, ct);
            }
        }
        catch (Exception ex)
        {
            var chatId = update.Message?.Chat.Id ?? update.CallbackQuery?.Message?.Chat.Id;
            logger.LogError(ex, "Unhandled error processing update from {ChatId}", chatId);
        }
    }

    private async Task HandleCallbackQueryAsync(ITelegramBotClient client, CallbackQuery query, CancellationToken ct)
    {
        var chatId = query.Message!.Chat.Id;
        var data = query.Data ?? string.Empty;

        await client.AnswerCallbackQuery(query.Id, cancellationToken: ct);

        switch (data)
        {
            case "pause":
                await userService.SetPausedAsync(chatId, true, ct);
                await client.EditMessageReplyMarkup(
                    chatId, query.Message.MessageId,
                    new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup(
                        Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton.WithCallbackData("▶️ Resume notifications", "resume")),
                    cancellationToken: ct);
                await client.SendMessage(chatId, "⏸ Notifications paused\\.", parseMode: ParseMode.MarkdownV2, cancellationToken: ct);
                break;

            case "resume":
                await userService.SetPausedAsync(chatId, false, ct);
                await client.EditMessageReplyMarkup(
                    chatId, query.Message.MessageId,
                    new Telegram.Bot.Types.ReplyMarkups.InlineKeyboardMarkup(
                        Telegram.Bot.Types.ReplyMarkups.InlineKeyboardButton.WithCallbackData("⏸ Pause notifications", "pause")),
                    cancellationToken: ct);
                await client.SendMessage(chatId, "▶️ Notifications resumed\\.", parseMode: ParseMode.MarkdownV2, cancellationToken: ct);
                break;

            default:
                logger.LogWarning("Unknown callback query data: {Data}", data);
                break;
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
