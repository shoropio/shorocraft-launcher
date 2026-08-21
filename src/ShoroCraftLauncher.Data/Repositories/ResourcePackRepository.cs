using Microsoft.EntityFrameworkCore;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Data.Database;

namespace ShoroCraftLauncher.Data.Repositories;

public class ResourcePackRepository : IResourcePackRepository
{
    private readonly IDbContextFactory<LauncherDbContext> _contextFactory;

    public ResourcePackRepository(IDbContextFactory<LauncherDbContext> contextFactory) => _contextFactory = contextFactory;

    public async Task<List<ResourcePack>> GetByProfileIdAsync(int profileId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        return await context.ResourcePacks.AsNoTracking().Where(r => r.ProfileId == profileId).OrderByDescending(r => r.AddedAt).ToListAsync().ConfigureAwait(false);
    }

    public async Task<ResourcePack?> GetByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        return await context.ResourcePacks.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id).ConfigureAwait(false);
    }

    public async Task<int> CreateAsync(ResourcePack pack)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        pack.AddedAt = DateTime.UtcNow;
        context.ResourcePacks.Add(pack);
        await context.SaveChangesAsync().ConfigureAwait(false);
        return pack.Id;
    }

    public async Task UpdateAsync(ResourcePack pack)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        context.ResourcePacks.Update(pack);
        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        var pack = await context.ResourcePacks.FindAsync(id).ConfigureAwait(false);
        if (pack != null)
        {
            context.ResourcePacks.Remove(pack);
            await context.SaveChangesAsync().ConfigureAwait(false);
        }
    }
}
