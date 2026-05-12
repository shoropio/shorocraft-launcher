using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Core.Interfaces;

public interface IModService
{
    Task<List<Mod>> GetModsAsync(int profileId);
    Task<Mod> AddModAsync(int profileId, string sourceFilePath);
    Task ToggleModAsync(int modId);
    Task RemoveModAsync(int modId);
    Task<string> GetModsFolderAsync(int profileId);
    Task<List<Mod>> SearchModrinthAsync(string query, string minecraftVersion, string loaderType);
}
