using Microsoft.EntityFrameworkCore;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Data.Database;

namespace ShoroCraftLauncher.Data.Repositories;

public class ScriptRepository : IScriptRepository
{
    private readonly IDbContextFactory<LauncherDbContext> _contextFactory;

    public ScriptRepository(IDbContextFactory<LauncherDbContext> contextFactory) => _contextFactory = contextFactory;

    public async Task<List<Script>> GetByProfileIdAsync(int profileId)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Scripts.AsNoTracking().Where(s => s.ProfileId == profileId).OrderByDescending(s => s.ModifiedAt).ToListAsync();
    }

    public async Task<Script?> GetByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Scripts.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<int> CreateAsync(Script script)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        script.CreatedAt = DateTime.UtcNow;
        script.ModifiedAt = DateTime.UtcNow;
        context.Scripts.Add(script);
        await context.SaveChangesAsync();
        return script.Id;
    }

    public async Task UpdateAsync(Script script)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        script.ModifiedAt = DateTime.UtcNow;
        context.Scripts.Update(script);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var script = await context.Scripts.FindAsync(id);
        if (script != null)
        {
            context.Scripts.Remove(script);
            await context.SaveChangesAsync();
        }
    }
}
