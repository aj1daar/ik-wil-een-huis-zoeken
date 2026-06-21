using IWEHZ.Bot.Conversations;

namespace IWEHZ.Tests.Bot;

public sealed class ConversationStateCacheTests
{
    private readonly ConversationStateCache _cache = new();

    [Fact]
    public void Get_UnknownChatId_ReturnsNone()
    {
        _cache.Get(999999L).Should().Be(ConversationStep.None);
    }

    [Fact]
    public void Set_ThenGet_ReturnsSameStep()
    {
        _cache.Set(1L, ConversationStep.AwaitingBudget);
        _cache.Get(1L).Should().Be(ConversationStep.AwaitingBudget);
    }

    [Fact]
    public void Set_OverwritesPreviousStep()
    {
        _cache.Set(1L, ConversationStep.AwaitingBudget);
        _cache.Set(1L, ConversationStep.AwaitingCities);
        _cache.Get(1L).Should().Be(ConversationStep.AwaitingCities);
    }

    [Fact]
    public void Clear_RemovesStep()
    {
        _cache.Set(1L, ConversationStep.AwaitingBudget);
        _cache.Clear(1L);
        _cache.Get(1L).Should().Be(ConversationStep.None);
    }

    [Fact]
    public void Clear_NonExistentChatId_DoesNotThrow()
    {
        var act = () => _cache.Clear(999999L);
        act.Should().NotThrow();
    }

    [Fact]
    public void MultipleUsers_StatesAreIsolated()
    {
        _cache.Set(1L, ConversationStep.AwaitingBudget);
        _cache.Set(2L, ConversationStep.AwaitingCities);
        _cache.Set(3L, ConversationStep.AwaitingSettingsChoice);

        _cache.Get(1L).Should().Be(ConversationStep.AwaitingBudget);
        _cache.Get(2L).Should().Be(ConversationStep.AwaitingCities);
        _cache.Get(3L).Should().Be(ConversationStep.AwaitingSettingsChoice);
    }

    [Fact]
    public void Clear_OneUser_DoesNotAffectOthers()
    {
        _cache.Set(1L, ConversationStep.AwaitingBudget);
        _cache.Set(2L, ConversationStep.AwaitingCities);

        _cache.Clear(1L);

        _cache.Get(1L).Should().Be(ConversationStep.None);
        _cache.Get(2L).Should().Be(ConversationStep.AwaitingCities);
    }

    [Fact]
    public void ConcurrentAccess_DoesNotThrow()
    {
        var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            _cache.Set(i, ConversationStep.AwaitingBudget);
            _ = _cache.Get(i);
            _cache.Clear(i);
        }));

        var act = () => Task.WhenAll(tasks);
        act.Should().NotThrowAsync();
    }
}
