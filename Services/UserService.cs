using IWEHZ.Domain.Models;
using IWEHZ.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IWEHZ.Services;

public sealed class UserService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<User?> GetByChatIdAsync(long chatId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users
            .Include(u => u.UserCities)
            .ThenInclude(uc => uc.City)
            .FirstOrDefaultAsync(u => u.TelegramChatId == chatId, ct);
    }

    public async Task<User> RegisterAsync(long chatId, string? username, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var existing = await db.Users.FirstOrDefaultAsync(u => u.TelegramChatId == chatId, ct);
        if (existing is not null) return existing;

        var user = new User
        {
            TelegramChatId = chatId,
            TelegramUsername = username,
            IsActive = true,
            OnboardingState = OnboardingState.AwaitingApproval,
            CreatedAt = DateTime.UtcNow,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task SetMinBudgetAsync(long chatId, decimal? minBudget, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Users
            .Where(u => u.TelegramChatId == chatId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.MinBudget, minBudget), ct);
    }

    public async Task SetBudgetAsync(long chatId, decimal budget, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Users
            .Where(u => u.TelegramChatId == chatId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.MaxBudget, budget), ct);
    }

    public async Task SetCitiesAsync(long chatId, IEnumerable<int> cityIds, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var userId = await db.Users
            .Where(u => u.TelegramChatId == chatId)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct);

        if (userId == 0) return;

        await db.UserCities.Where(uc => uc.UserId == userId).ExecuteDeleteAsync(ct);

        foreach (var id in cityIds)
            db.UserCities.Add(new UserCity { UserId = userId, CityId = id });

        await db.SaveChangesAsync(ct);
    }

    public async Task CompleteOnboardingAsync(long chatId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Users
            .Where(u => u.TelegramChatId == chatId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.OnboardingState, OnboardingState.Completed), ct);
    }

    public async Task SetPropertyTypeFilterAsync(long chatId, PropertyTypeFilter filter, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Users
            .Where(u => u.TelegramChatId == chatId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PropertyTypeFilter, filter), ct);
    }

    public async Task SetPausedAsync(long chatId, bool paused, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Users
            .Where(u => u.TelegramChatId == chatId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsPaused, paused), ct);
    }

    public async Task ActivateAsync(long chatId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Users
            .Where(u => u.TelegramChatId == chatId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.IsActive, true)
                .SetProperty(u => u.OnboardingState, OnboardingState.AwaitingBudget), ct);
    }
}
