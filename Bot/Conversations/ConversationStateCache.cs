using System.Collections.Concurrent;

namespace IWEHZ.Bot.Conversations;

public enum ConversationStep
{
    None,
    AwaitingMinBudget,
    AwaitingBudget,
    AwaitingPropertyType,
    AwaitingCities,
    AwaitingSettingsChoice,
    AwaitingNewBudget,
    AwaitingNewCities,
    AwaitingNewMinBudget,
}

public sealed class ConversationStateCache
{
    private readonly ConcurrentDictionary<long, ConversationStep> _steps = new();

    public ConversationStep Get(long chatId) =>
        _steps.TryGetValue(chatId, out var step) ? step : ConversationStep.None;

    public void Set(long chatId, ConversationStep step) =>
        _steps[chatId] = step;

    public void Clear(long chatId) =>
        _steps.TryRemove(chatId, out _);
}
