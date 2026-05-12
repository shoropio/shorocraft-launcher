using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Core.Interfaces;

public interface IResourcePackRepository
{
    Task<List<ResourcePack>> GetByProfileIdAsync(int profileId);
    Task<ResourcePack?> GetByIdAsync(int id);
    Task<int> CreateAsync(ResourcePack pack);
    Task UpdateAsync(ResourcePack pack);
    Task DeleteAsync(int id);
}
