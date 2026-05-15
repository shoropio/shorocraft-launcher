using Microsoft.EntityFrameworkCore;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Data.Database;

namespace ShoroCraftLauncher.Data.Repositories;

public class GameVersionRepository : IGameVersionRepository
{
    private readonly IDbContextFactory<LauncherDbContext> _contextFactory;

    public GameVersionRepository(IDbContextFactory<LauncherDbContext> contextFactory) => _contextFactory = contextFactory;

    public async Task<List<GameVersion>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.GameVersions.AsNoTracking().OrderByDescending(g => g.ReleasedAt).ToListAsync();
    }

    public async Task<GameVersion?> GetByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.GameVersions.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id);
    }

    public async Task<GameVersion?> GetByVersionIdAsync(string versionId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.GameVersions.AsNoTracking().FirstOrDefaultAsync(g => g.VersionId == versionId);
    }

    public async Task<int> CreateAsync(GameVersion version)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.GameVersions.Add(version);
        await context.SaveChangesAsync();
        return version.Id;
    }

    public async Task UpdateAsync(GameVersion version)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        version.UpdatedAt = DateTime.UtcNow;
        context.GameVersions.Update(version);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var version = await context.GameVersions.FindAsync(id);
        if (version != null)
        {
            context.GameVersions.Remove(version);
            await context.SaveChangesAsync();
        }
    }
}
