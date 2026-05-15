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
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.ShaderPacks.AsNoTracking().Where(s => s.ProfileId == profileId).OrderByDescending(s => s.AddedAt).ToListAsync();
    }

    public async Task<ShaderPack?> GetByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.ShaderPacks.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<int> CreateAsync(ShaderPack pack)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        pack.AddedAt = DateTime.UtcNow;
        context.ShaderPacks.Add(pack);
        await context.SaveChangesAsync();
        return pack.Id;
    }

    public async Task UpdateAsync(ShaderPack pack)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.ShaderPacks.Update(pack);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var pack = await context.ShaderPacks.FindAsync(id);
        if (pack != null)
        {
            context.ShaderPacks.Remove(pack);
            await context.SaveChangesAsync();
        }
    }
}
