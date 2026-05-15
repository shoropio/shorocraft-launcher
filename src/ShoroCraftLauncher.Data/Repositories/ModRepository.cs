using Microsoft.EntityFrameworkCore;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Data.Database;

namespace ShoroCraftLauncher.Data.Repositories;

public class ModRepository : IModRepository
{
    private readonly IDbContextFactory<LauncherDbContext> _contextFactory;

    public ModRepository(IDbContextFactory<LauncherDbContext> contextFactory) => _contextFactory = contextFactory;

    public async Task<List<Mod>> GetByProfileIdAsync(int profileId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Mods.AsNoTracking().Where(m => m.ProfileId == profileId).OrderByDescending(m => m.AddedAt).ToListAsync();
    }

    public async Task<Mod?> GetByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Mods.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<int> CreateAsync(Mod mod)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        mod.AddedAt = DateTime.UtcNow;
        context.Mods.Add(mod);
        await context.SaveChangesAsync();
        return mod.Id;
    }

    public async Task UpdateAsync(Mod mod)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        context.Mods.Update(mod);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var mod = await context.Mods.FindAsync(id);
        if (mod != null)
        {
            context.Mods.Remove(mod);
            await context.SaveChangesAsync();
        }
    }

    public async Task DeleteByProfileIdAsync(int profileId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var mods = await context.Mods.Where(m => m.ProfileId == profileId).ToListAsync();
        context.Mods.RemoveRange(mods);
        await context.SaveChangesAsync();
    }
}
