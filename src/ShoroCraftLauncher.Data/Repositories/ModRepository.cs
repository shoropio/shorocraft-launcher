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
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        return await context.Mods.AsNoTracking().Where(m => m.ProfileId == profileId).OrderByDescending(m => m.AddedAt).ToListAsync().ConfigureAwait(false);
    }

    public async Task<Mod?> GetByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        return await context.Mods.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id).ConfigureAwait(false);
    }

    public async Task<int> CreateAsync(Mod mod)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        mod.AddedAt = DateTime.UtcNow;
        context.Mods.Add(mod);
        await context.SaveChangesAsync().ConfigureAwait(false);
        return mod.Id;
    }

    public async Task UpdateAsync(Mod mod)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        context.Mods.Update(mod);
        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        var mod = await context.Mods.FindAsync(id).ConfigureAwait(false);
        if (mod != null)
        {
            context.Mods.Remove(mod);
            await context.SaveChangesAsync().ConfigureAwait(false);
        }
    }

    public async Task DeleteByProfileIdAsync(int profileId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        var mods = await context.Mods.Where(m => m.ProfileId == profileId).ToListAsync().ConfigureAwait(false);
        context.Mods.RemoveRange(mods);
        await context.SaveChangesAsync().ConfigureAwait(false);
    }
}
