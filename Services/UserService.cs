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
            .AsNoTracking()
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
            IsActive = false,
            OnboardingState = OnboardingState.AwaitingApproval,
            CreatedAt = DateTime.UtcNow,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task SetBudgetAsync(long chatId, decimal budget, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.TelegramChatId == chatId, ct);
        if (user is null) return;
        user.MaxBudget = budget;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetCitiesAsync(long chatId, IEnumerable<int> cityIds, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users
            .Include(u => u.UserCities)
            .FirstOrDefaultAsync(u => u.TelegramChatId == chatId, ct);
        if (user is null) return;

        user.UserCities.Clear();
        foreach (var id in cityIds)
            user.UserCities.Add(new UserCity { UserId = user.Id, CityId = id });

        await db.SaveChangesAsync(ct);
    }

    public async Task CompleteOnboardingAsync(long chatId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.TelegramChatId == chatId, ct);
        if (user is null) return;
        user.OnboardingState = OnboardingState.Completed;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetPausedAsync(long chatId, bool paused, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.TelegramChatId == chatId, ct);
        if (user is null) return;
        user.IsPaused = paused;
        await db.SaveChangesAsync(ct);
    }

    public async Task ActivateAsync(long chatId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.TelegramChatId == chatId, ct);
        if (user is null) return;
        user.IsActive = true;
        user.OnboardingState = OnboardingState.AwaitingBudget;
        await db.SaveChangesAsync(ct);
    }
}
