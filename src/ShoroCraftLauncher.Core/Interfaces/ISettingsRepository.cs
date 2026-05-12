using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Core.Interfaces;

public interface ISettingsRepository
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    Task<Dictionary<string, string>> GetAllAsync();
}
