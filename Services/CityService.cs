using IWEHZ.Domain.Models;
using IWEHZ.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IWEHZ.Services;

public sealed class CityService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<City?> FindByNameAsync(string input, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var normalized = input.Trim().ToLowerInvariant();

        return await db.Cities
            .AsNoTracking()
            .Where(c => c.IsActive &&
                (c.NameNl.ToLower() == normalized || c.NameEn.ToLower() == normalized))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<City>> GetAllActiveAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Cities.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.NameNl).ToListAsync(ct);
    }
}
