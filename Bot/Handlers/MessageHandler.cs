using IWEHZ.Bot.Conversations;
using IWEHZ.Infrastructure.Markdown;
using IWEHZ.Infrastructure.Persistence;
using IWEHZ.Services;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using DomainUser = IWEHZ.Domain.Models.User;
using OnboardingState = IWEHZ.Domain.Models.OnboardingState;

namespace IWEHZ.Bot.Handlers;

public sealed class MessageHandler(
    UserService userService,
    CityService cityService,
    ConversationStateCache stateCache,
    IDbContextFactory<AppDbContext> dbFactory,
    IConfiguration config,
    ILogger<MessageHandler> logger)
{
    private long AdminChatId => config.GetValue<long>("Telegram:AdminChatId");

    public async Task HandleAsync(ITelegramBotClient bot, Message message, CancellationToken ct)
    {
        if (message.From is null || message.Text is null) return;

        var chatId = message.Chat.Id;
        var text = message.Text.Trim();

        if (text.Length > 4096) return;

        var username = message.From.Username;

        if (text == "/stats" && chatId == AdminChatId)
        {
            await HandleStatsAsync(bot, ct);
            return;
        }

        if (text.StartsWith("/activate") && chatId == AdminChatId)
        {
            await HandleActivateCommandAsync(bot, text, ct);
            return;
        }

        var user = await userService.GetByChatIdAsync(chatId, ct);

        if (user is null)
            user = await userService.RegisterAsync(chatId, username, ct);

        if (!user.IsActive)
        {
            if (chatId == AdminChatId)
            {
                await userService.ActivateAsync(chatId, ct);
                user = await userService.GetByChatIdAsync(chatId, ct) ?? user;
            }
            else
            {
                await bot.SendMessage(
                    chatId,
                    "👋 Welcome\\! To use this bot, please contact @atainogoibay to request access\\.",
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                    cancellationToken: ct);
                return;
            }
        }

        var step = stateCache.Get(chatId);

        if (text == "/start")
        {
            await HandleStartAsync(bot, user, chatId, ct);
            return;
        }

        if (text == "/settings")
        {
            await HandleSettingsAsync(bot, chatId, ct);
            return;
        }

        if (text == "/mycities")
        {
            await HandleMyCitiesAsync(bot, user, chatId, ct);
            return;
        }

        if (text == "/status")
        {
            await HandleStatusAsync(bot, chatId, ct);
            return;
        }

        if (text == "/help")
        {
            await HandleHelpAsync(bot, chatId, ct);
            return;
        }

        if (text == "/pause")
        {
            await HandlePauseAsync(bot, chatId, true, ct);
            return;
        }

        if (text == "/resume")
        {
            await HandlePauseAsync(bot, chatId, false, ct);
            return;
        }

        switch (step)
        {
            case ConversationStep.AwaitingBudget:
            case ConversationStep.AwaitingNewBudget:
                await HandleBudgetInputAsync(bot, chatId, text, step, ct);
                break;

            case ConversationStep.AwaitingNewMinBudget:
                await HandleMinBudgetInputAsync(bot, chatId, text, ct);
                break;

            case ConversationStep.AwaitingCities:
            case ConversationStep.AwaitingNewCities:
                await HandleCitiesInputAsync(bot, chatId, text, step, ct);
                break;

            case ConversationStep.AwaitingSettingsChoice:
                await HandleSettingsChoiceAsync(bot, chatId, text, ct);
                break;

            default:
                await bot.SendMessage(chatId,
                    "Use /help to see all available commands\\.",
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                    cancellationToken: ct);
                break;
        }
    }

    private async Task HandleStartAsync(ITelegramBotClient bot, DomainUser user, long chatId, CancellationToken ct)
    {
        if (user.OnboardingState == OnboardingState.Completed)
        {
            await bot.SendMessage(chatId,
                "You are already set up\\! Use /settings to update your preferences\\.",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                cancellationToken: ct);
            return;
        }

        stateCache.Set(chatId, ConversationStep.AwaitingBudget);

        var keyboard = new ReplyKeyboardMarkup(
        [
            [new KeyboardButton("€1000"), new KeyboardButton("€1500")],
            [new KeyboardButton("€2000"), new KeyboardButton("€2500")],
            [new KeyboardButton("€3000")],
        ])
        { ResizeKeyboard = true, OneTimeKeyboard = true };

        await bot.SendMessage(chatId,
            "👋 Let's get you set up\\!\n\nWhat is your *maximum monthly budget*?",
            parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task HandleBudgetInputAsync(
        ITelegramBotClient bot, long chatId, string text,
        ConversationStep currentStep, CancellationToken ct)
    {
        if (!BudgetParser.TryParse(text, out var budget))
        {
            await bot.SendMessage(chatId,
                "Please enter a valid budget between €100 and €10,000\\.",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                cancellationToken: ct);
            return;
        }

        await userService.SetBudgetAsync(chatId, budget, ct);

        var nextStep = currentStep == ConversationStep.AwaitingBudget
            ? ConversationStep.AwaitingCities
            : ConversationStep.AwaitingNewCities;

        stateCache.Set(chatId, nextStep);

        var cities = await cityService.GetAllActiveAsync(ct);
        var cityList = string.Join(", ", cities.Select(c =>
            c.NameNl == c.NameEn ? c.NameNl : $"{c.NameNl} / {c.NameEn}"));

        await bot.SendMessage(chatId,
            $"✅ Budget set to €{budget:N0}/month\\.\n\n" +
            $"Now, which *cities* are you looking in?\n\n" +
            $"Type the city names separated by commas, in Dutch or English\\.\n\n" +
            $"*Available cities:*\n{MarkdownHelper.EscapeV2(cityList)}",
            parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
            replyMarkup: new ReplyKeyboardRemove(),
            cancellationToken: ct);
    }

    private async Task HandleCitiesInputAsync(
        ITelegramBotClient bot, long chatId, string text,
        ConversationStep currentStep, CancellationToken ct)
    {
        var inputs = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (inputs.Length == 0 || inputs.Length > 25)
        {
            await bot.SendMessage(chatId,
                "Please enter between 1 and 25 cities\\.",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                cancellationToken: ct);
            return;
        }

        if (inputs.Any(i => i.Length > 100))
        {
            await bot.SendMessage(chatId,
                "City names must be under 100 characters\\.",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                cancellationToken: ct);
            return;
        }

        var resolvedIds = new List<int>();
        var unknowns = new List<string>();

        foreach (var input in inputs)
        {
            var city = await cityService.FindByNameAsync(input, ct);
            if (city is not null)
                resolvedIds.Add(city.Id);
            else
                unknowns.Add(input);
        }

        if (unknowns.Count > 0)
        {
            var unknownList = string.Join(", ", unknowns.Select(MarkdownHelper.EscapeV2));
            var all = await cityService.GetAllActiveAsync(ct);
            var available = string.Join(", ", all.Select(c =>
                c.NameNl == c.NameEn ? c.NameNl : $"{c.NameNl}/{c.NameEn}"));

            await bot.SendMessage(chatId,
                $"❌ Unknown cities: *{unknownList}*\\.\n\n" +
                $"Please use names from this list:\n{MarkdownHelper.EscapeV2(available)}",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                cancellationToken: ct);
            return;
        }

        await userService.SetCitiesAsync(chatId, resolvedIds, ct);
        stateCache.Clear(chatId);

        if (currentStep == ConversationStep.AwaitingCities)
        {
            await userService.CompleteOnboardingAsync(chatId, ct);
            await bot.SendMessage(chatId,
                "🎉 You're all set\\! You'll receive alerts when new listings match your criteria\\.",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                replyMarkup: new ReplyKeyboardRemove(),
                cancellationToken: ct);
        }
        else
        {
            await bot.SendMessage(chatId,
                "✅ Cities updated\\!",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                replyMarkup: new ReplyKeyboardRemove(),
                cancellationToken: ct);
        }
    }

    private async Task HandleSettingsAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        stateCache.Set(chatId, ConversationStep.AwaitingSettingsChoice);

        var keyboard = new ReplyKeyboardMarkup(
        [
            [new KeyboardButton("Update budget"), new KeyboardButton("Update min budget")],
            [new KeyboardButton("Update cities"), new KeyboardButton("Update property type")],
            [new KeyboardButton("Cancel")],
        ])
        { ResizeKeyboard = true, OneTimeKeyboard = true };

        await bot.SendMessage(chatId,
            "⚙️ *Settings* — what would you like to update?",
            parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    private async Task HandleSettingsChoiceAsync(ITelegramBotClient bot, long chatId, string text, CancellationToken ct)
    {
        if (text.Equals("Update budget", StringComparison.OrdinalIgnoreCase))
        {
            stateCache.Set(chatId, ConversationStep.AwaitingNewBudget);

            var keyboard = new ReplyKeyboardMarkup(
            [
                [new KeyboardButton("€1000"), new KeyboardButton("€1500")],
                [new KeyboardButton("€2000"), new KeyboardButton("€2500")],
                [new KeyboardButton("€3000")],
            ])
            { ResizeKeyboard = true, OneTimeKeyboard = true };

            await bot.SendMessage(chatId,
                "Enter your new maximum monthly budget:",
                replyMarkup: keyboard,
                cancellationToken: ct);
        }
        else if (text.Equals("Update min budget", StringComparison.OrdinalIgnoreCase))
        {
            stateCache.Set(chatId, ConversationStep.AwaitingNewMinBudget);

            var keyboard = new ReplyKeyboardMarkup(
            [
                [new KeyboardButton("€500"), new KeyboardButton("€700"), new KeyboardButton("€800")],
                [new KeyboardButton("€1000"), new KeyboardButton("No minimum")],
            ])
            { ResizeKeyboard = true, OneTimeKeyboard = true };

            await bot.SendMessage(chatId,
                "Enter your *minimum monthly budget* \\(or 'No minimum'\\):",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                replyMarkup: keyboard,
                cancellationToken: ct);
        }
        else if (text.Equals("Update property type", StringComparison.OrdinalIgnoreCase))
        {
            stateCache.Clear(chatId);
            var keyboard = new InlineKeyboardMarkup(
            [
                [
                    InlineKeyboardButton.WithCallbackData("🏠 Any", "proptype:Any"),
                    InlineKeyboardButton.WithCallbackData("🏢 Apartment", "proptype:Apartment"),
                ],
                [
                    InlineKeyboardButton.WithCallbackData("🏡 House", "proptype:House"),
                    InlineKeyboardButton.WithCallbackData("🛏 Room", "proptype:Room"),
                ],
            ]);
            await bot.SendMessage(chatId,
                "Select the *property type* you want to be notified about:",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                replyMarkup: new ReplyKeyboardRemove(),
                cancellationToken: ct);
            await bot.SendMessage(chatId, "👇", replyMarkup: keyboard, cancellationToken: ct);
        }
        else if (text.Equals("Update cities", StringComparison.OrdinalIgnoreCase))
        {
            stateCache.Set(chatId, ConversationStep.AwaitingNewCities);

            var cities = await cityService.GetAllActiveAsync(ct);
            var cityList = string.Join(", ", cities.Select(c =>
                c.NameNl == c.NameEn ? c.NameNl : $"{c.NameNl}/{c.NameEn}"));

            await bot.SendMessage(chatId,
                $"Enter the new cities separated by commas:\n\n{MarkdownHelper.EscapeV2(cityList)}",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                replyMarkup: new ReplyKeyboardRemove(),
                cancellationToken: ct);
        }
        else
        {
            stateCache.Clear(chatId);
            await bot.SendMessage(chatId, "Cancelled\\.",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                replyMarkup: new ReplyKeyboardRemove(),
                cancellationToken: ct);
        }
    }

    private async Task HandleMinBudgetInputAsync(ITelegramBotClient bot, long chatId, string text, CancellationToken ct)
    {
        decimal? minBudget = null;

        if (!text.Equals("No minimum", StringComparison.OrdinalIgnoreCase))
        {
            if (!BudgetParser.TryParse(text, out var parsed))
            {
                await bot.SendMessage(chatId,
                    "Please enter a valid amount or 'No minimum'\\.",
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                    cancellationToken: ct);
                return;
            }
            minBudget = parsed;
        }

        await userService.SetMinBudgetAsync(chatId, minBudget, ct);
        stateCache.Clear(chatId);

        var confirmation = minBudget.HasValue
            ? $"✅ Minimum budget set to €{minBudget.Value:N0}/month\\."
            : "✅ Minimum budget removed\\.";

        await bot.SendMessage(chatId, confirmation,
            parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
            replyMarkup: new ReplyKeyboardRemove(),
            cancellationToken: ct);
    }

    private async Task HandlePauseAsync(ITelegramBotClient bot, long chatId, bool pause, CancellationToken ct)
    {
        await userService.SetPausedAsync(chatId, pause, ct);

        var toggleButton = new InlineKeyboardMarkup(
            InlineKeyboardButton.WithCallbackData(
                pause ? "▶️ Resume notifications" : "⏸ Pause notifications",
                pause ? "resume" : "pause"));

        await bot.SendMessage(chatId,
            pause
                ? "⏸ *Notifications paused*\\. You won't receive any alerts until you resume\\."
                : "▶️ *Notifications resumed*\\. You'll receive alerts again\\.",
            parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
            replyMarkup: toggleButton,
            cancellationToken: ct);
    }

    private static async Task HandleHelpAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        await bot.SendMessage(chatId,
            "📋 *Available commands*\n\n" +
            "/start \\— Set up your profile\n" +
            "/status \\— View your current settings\n" +
            "/settings \\— Update budget or cities\n" +
            "/pause \\— Pause notifications temporarily\n" +
            "/resume \\— Resume notifications\n" +
            "/mycities \\— View your saved cities\n" +
            "/help \\— Show this message",
            parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
            cancellationToken: ct);
    }

    private async Task HandleStatusAsync(ITelegramBotClient bot, long chatId, CancellationToken ct)
    {
        var user = await userService.GetByChatIdAsync(chatId, ct);
        if (user is null) return;

        if (user.OnboardingState != OnboardingState.Completed)
        {
            await bot.SendMessage(chatId,
                "⚠️ You haven't completed setup yet\\. Send /start to begin\\.",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                cancellationToken: ct);
            return;
        }

        var cities = user.UserCities?.Select(uc => uc.City.NameNl).ToList() ?? [];
        var cityList = cities.Count > 0
            ? string.Join(", ", cities)
            : "none";

        var minBudget = user.MinBudget.HasValue ? $"€{user.MinBudget.Value:N0}" : "none";
        var budget = user.MaxBudget.HasValue
            ? $"€{user.MaxBudget.Value:N0}/month"
            : "no limit";

        var pauseState = user.IsPaused ? "⏸ Paused" : "▶️ Active";
        var propType = user.PropertyTypeFilter.ToString();
        var toggleButton = new InlineKeyboardMarkup(
            InlineKeyboardButton.WithCallbackData(
                user.IsPaused ? "▶️ Resume notifications" : "⏸ Pause notifications",
                user.IsPaused ? "resume" : "pause"));

        await bot.SendMessage(chatId,
            $"📊 *Your status*\n\n" +
            $"🔔 *Notifications:* {MarkdownHelper.EscapeV2(pauseState)}\n" +
            $"💶 *Budget:* {MarkdownHelper.EscapeV2(minBudget)} – {MarkdownHelper.EscapeV2(budget)}\n" +
            $"🏠 *Property type:* {MarkdownHelper.EscapeV2(propType)}\n" +
            $"📍 *Cities:* {MarkdownHelper.EscapeV2(cityList)}\n\n" +
            $"Use /settings to update your preferences\\.",
            parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
            replyMarkup: toggleButton,
            cancellationToken: ct);
    }

    private async Task HandleMyCitiesAsync(ITelegramBotClient bot, DomainUser user, long chatId, CancellationToken ct)
    {
        var refreshed = await userService.GetByChatIdAsync(chatId, ct);
        if (refreshed?.UserCities is null || refreshed.UserCities.Count == 0)
        {
            await bot.SendMessage(chatId, "You have no cities saved yet\\.",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                cancellationToken: ct);
            return;
        }

        var list = string.Join("\n", refreshed.UserCities.Select(uc => $"• {uc.City.NameNl}"));
        await bot.SendMessage(chatId,
            $"📍 *Your cities:*\n{MarkdownHelper.EscapeV2(list)}\n\n💶 *Max budget:* €{refreshed.MaxBudget:N0}/month",
            parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
            cancellationToken: ct);
    }

    private async Task HandleStatsAsync(ITelegramBotClient bot, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var totalUsers = await db.Users.CountAsync(ct);
        var activeUsers = await db.Users.CountAsync(u => u.IsActive && u.OnboardingState == OnboardingState.Completed, ct);
        var pausedUsers = await db.Users.CountAsync(u => u.IsPaused, ct);
        var totalListings = await db.RentalListings.CountAsync(ct);

        var perSource = await db.RentalListings
            .GroupBy(l => l.Source)
            .Select(g => new { Source = g.Key, Count = g.Count(), Latest = g.Max(l => l.ScrapedAt) })
            .OrderByDescending(x => x.Count)
            .ToListAsync(ct);

        var lines = new System.Text.StringBuilder();
        lines.AppendLine("📊 *Bot stats*\n");
        lines.AppendLine($"👥 Users: {activeUsers} active / {totalUsers} total / {pausedUsers} paused");
        lines.AppendLine($"🏠 Listings in DB: {totalListings}\n");
        lines.AppendLine("*Per source:*");

        foreach (var s in perSource)
            lines.AppendLine($"• {MarkdownHelper.EscapeV2(s.Source)}: {s.Count} listings \\(last: {MarkdownHelper.EscapeV2(s.Latest.ToString("MM/dd HH:mm"))} UTC\\)");

        await bot.SendMessage(AdminChatId,
            lines.ToString().TrimEnd(),
            parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
            cancellationToken: ct);
    }

    private async Task HandleActivateCommandAsync(ITelegramBotClient bot, string text, CancellationToken ct)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !long.TryParse(parts[1], out var targetChatId))
        {
            await bot.SendMessage(AdminChatId, "Usage: /activate <telegram_chat_id>", cancellationToken: ct);
            return;
        }

        await userService.ActivateAsync(targetChatId, ct);
        await bot.SendMessage(AdminChatId, $"✅ User {targetChatId} activated.", cancellationToken: ct);

        try
        {
            await bot.SendMessage(targetChatId,
                "✅ Your access has been approved\\! Use /start to begin setup\\.",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.MarkdownV2,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not notify activated user {ChatId}", targetChatId);
        }
    }
}
