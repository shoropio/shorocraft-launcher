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
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.GameMaps.AsNoTracking().Where(m => m.ProfileId == profileId).OrderByDescending(m => m.AddedAt).ToListAsync();
    }

    public async Task<GameMap?> GetByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.GameMaps.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<int> CreateAsync(GameMap map)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        map.AddedAt = DateTime.UtcNow;
        context.GameMaps.Add(map);
        await context.SaveChangesAsync();
        return map.Id;
    }

    public async Task UpdateAsync(GameMap map)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.GameMaps.Update(map);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var map = await context.GameMaps.FindAsync(id);
        if (map != null)
        {
            context.GameMaps.Remove(map);
            await context.SaveChangesAsync();
        }
    }
}
