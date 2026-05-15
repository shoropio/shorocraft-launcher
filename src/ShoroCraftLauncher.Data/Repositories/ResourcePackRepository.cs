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
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.ResourcePacks.AsNoTracking().Where(r => r.ProfileId == profileId).OrderByDescending(r => r.AddedAt).ToListAsync();
    }

    public async Task<ResourcePack?> GetByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.ResourcePacks.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<int> CreateAsync(ResourcePack pack)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        pack.AddedAt = DateTime.UtcNow;
        context.ResourcePacks.Add(pack);
        await context.SaveChangesAsync();
        return pack.Id;
    }

    public async Task UpdateAsync(ResourcePack pack)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.ResourcePacks.Update(pack);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var pack = await context.ResourcePacks.FindAsync(id);
        if (pack != null)
        {
            context.ResourcePacks.Remove(pack);
            await context.SaveChangesAsync();
        }
    }
}
