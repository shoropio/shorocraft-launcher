using Microsoft.EntityFrameworkCore;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Data.Database;

namespace ShoroCraftLauncher.Data.Repositories;

public class SettingsRepository : ISettingsRepository
{
    private readonly IDbContextFactory<LauncherDbContext> _contextFactory;

    public SettingsRepository(IDbContextFactory<LauncherDbContext> contextFactory) => _contextFactory = contextFactory;

    public async Task<string?> GetAsync(string key)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var setting = await context.LauncherSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key);
        return setting?.Value;
    }

    public async Task SetAsync(string key, string value)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var setting = await context.LauncherSettings.FindAsync(key);
        if (setting != null)
        {
            setting.Value = value;
            context.LauncherSettings.Update(setting);
        }
        else
        {
            context.LauncherSettings.Add(new LauncherSetting { Key = key, Value = value });
        }
        await context.SaveChangesAsync();
    }

    public async Task<Dictionary<string, string>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.LauncherSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value);
    }
}
