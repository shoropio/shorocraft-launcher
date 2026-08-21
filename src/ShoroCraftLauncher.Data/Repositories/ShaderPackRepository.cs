using Microsoft.EntityFrameworkCore;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Data.Database;

namespace ShoroCraftLauncher.Data.Repositories;

public class ShaderPackRepository : IShaderPackRepository
{
    private readonly IDbContextFactory<LauncherDbContext> _contextFactory;

    public ShaderPackRepository(IDbContextFactory<LauncherDbContext> contextFactory) => _contextFactory = contextFactory;

    public async Task<List<ShaderPack>> GetByProfileIdAsync(int profileId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        return await context.ShaderPacks.AsNoTracking().Where(s => s.ProfileId == profileId).OrderByDescending(s => s.AddedAt).ToListAsync().ConfigureAwait(false);
    }

    public async Task<ShaderPack?> GetByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        return await context.ShaderPacks.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id).ConfigureAwait(false);
    }

    public async Task<int> CreateAsync(ShaderPack pack)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        pack.AddedAt = DateTime.UtcNow;
        context.ShaderPacks.Add(pack);
        await context.SaveChangesAsync().ConfigureAwait(false);
        return pack.Id;
    }

    public async Task UpdateAsync(ShaderPack pack)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        context.ShaderPacks.Update(pack);
        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        var pack = await context.ShaderPacks.FindAsync(id).ConfigureAwait(false);
        if (pack != null)
        {
            context.ShaderPacks.Remove(pack);
            await context.SaveChangesAsync().ConfigureAwait(false);
        }
    }
}
