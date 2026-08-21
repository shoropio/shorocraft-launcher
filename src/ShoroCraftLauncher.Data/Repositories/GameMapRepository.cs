using Microsoft.EntityFrameworkCore;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Data.Database;

namespace ShoroCraftLauncher.Data.Repositories;

public class GameMapRepository : IGameMapRepository
{
    private readonly IDbContextFactory<LauncherDbContext> _contextFactory;

    public GameMapRepository(IDbContextFactory<LauncherDbContext> contextFactory) => _contextFactory = contextFactory;

    public async Task<List<GameMap>> GetByProfileIdAsync(int profileId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        return await context.GameMaps.AsNoTracking().Where(m => m.ProfileId == profileId).OrderByDescending(m => m.AddedAt).ToListAsync().ConfigureAwait(false);
    }

    public async Task<GameMap?> GetByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        return await context.GameMaps.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id).ConfigureAwait(false);
    }

    public async Task<int> CreateAsync(GameMap map)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        map.AddedAt = DateTime.UtcNow;
        context.GameMaps.Add(map);
        await context.SaveChangesAsync().ConfigureAwait(false);
        return map.Id;
    }

    public async Task UpdateAsync(GameMap map)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        context.GameMaps.Update(map);
        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        var map = await context.GameMaps.FindAsync(id).ConfigureAwait(false);
        if (map != null)
        {
            context.GameMaps.Remove(map);
            await context.SaveChangesAsync().ConfigureAwait(false);
        }
    }
}
