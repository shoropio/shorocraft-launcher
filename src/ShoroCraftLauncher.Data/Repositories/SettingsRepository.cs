using Microsoft.EntityFrameworkCore;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Data.Database;

namespace ShoroCraftLauncher.Data.Repositories;

public class SettingsRepository : ISettingsRepository
{
    private readonly LauncherDbContext _context;

    public SettingsRepository(LauncherDbContext context) => _context = context;

    public async Task<string?> GetAsync(string key)
    {
        var setting = await _context.LauncherSettings.FindAsync(key);
        return setting?.Value;
    }

    public async Task SetAsync(string key, string value)
    {
        var setting = await _context.LauncherSettings.FindAsync(key);
        if (setting != null)
        {
            setting.Value = value;
            _context.LauncherSettings.Update(setting);
        }
        else
        {
            _context.LauncherSettings.Add(new LauncherSetting { Key = key, Value = value });
        }
        await _context.SaveChangesAsync();
    }

    public async Task<Dictionary<string, string>> GetAllAsync() =>
        await _context.LauncherSettings.ToDictionaryAsync(s => s.Key, s => s.Value);
}
