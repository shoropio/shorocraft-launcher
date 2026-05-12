using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Core.Interfaces;

public interface IResourcePackService
{
    Task<List<ResourcePack>> GetPacksAsync(int profileId);
    Task<ResourcePack> AddPackAsync(int profileId, string sourceFilePath);
    Task TogglePackAsync(int packId);
    Task RemovePackAsync(int packId);
    Task<string> GetPacksFolderAsync(int profileId);
}
