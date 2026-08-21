using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Data.Database;

namespace ShoroCraftLauncher.Data.Repositories;

public class SettingsRepository : ISettingsRepository
{
    private readonly IDbContextFactory<LauncherDbContext> _contextFactory;
    private readonly ISecretStorage _secretStorage;
    private readonly ILogger<SettingsRepository> _logger;

    public SettingsRepository(IDbContextFactory<LauncherDbContext> contextFactory, ISecretStorage secretStorage, ILogger<SettingsRepository> logger)
    {
        _contextFactory = contextFactory;
        _secretStorage = secretStorage;
        _logger = logger;
    }

    public async Task<string?> GetAsync(string key)
    {
        // First try to get from secret storage (for API keys migrated from DPAPI)
        if (key == "curseforge_api_key")
        {
            var secret = await _secretStorage.GetSecretAsync(key).ConfigureAwait(false);
            if (secret != null)
            {
                _logger.LogInformation("CurseForge API key retrieved from secure storage");
                return secret;
            }
            _logger.LogDebug("CurseForge API key not found in secure storage, falling back to database");
        }

        // Fallback: read from database for non-sensitive or non-migrated settings
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        var setting = await context.LauncherSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key).ConfigureAwait(false);
        return setting?.Value;
    }

    public async Task SetAsync(string key, string value)
    {
        if (key == "curseforge_api_key")
        {
            // Store sensitive API key in Windows Credential Locker instead of plain DB
            await _secretStorage.SetSecretAsync(key, value).ConfigureAwait(false);
            // Also remove from database after migration
            await RemoveFromDatabaseAsync(key).ConfigureAwait(false);
            _logger.LogInformation("CurseForge API key stored in secure Windows Credential Locker");
            return;
        }

        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        var setting = await context.LauncherSettings.FindAsync(key).ConfigureAwait(false);
        if (setting != null)
        {
            setting.Value = value;
            context.LauncherSettings.Update(setting);
        }
        else
        {
            context.LauncherSettings.Add(new LauncherSetting { Key = key, Value = value });
        }
        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task<Dictionary<string, string>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        var all = await context.LauncherSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value).ConfigureAwait(false);

        // Don't return sensitive API key from database
        if (all.ContainsKey("curseforge_api_key"))
        {
            all.Remove("curseforge_api_key");
        }

        return all;
    }

    public async Task RemoveFromDatabaseAsync(string key)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        var entity = await context.LauncherSettings.FirstOrDefaultAsync(s => s.Key == key).ConfigureAwait(false);
        if (entity != null)
        {
            context.LauncherSettings.Remove(entity);
            await context.SaveChangesAsync().ConfigureAwait(false);
        }
    }
}
