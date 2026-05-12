using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Core.Interfaces;

public interface IGameVersionRepository
{
    Task<List<GameVersion>> GetAllAsync();
    Task<GameVersion?> GetByIdAsync(int id);
    Task<GameVersion?> GetByVersionIdAsync(string versionId);
    Task<int> CreateAsync(GameVersion version);
    Task UpdateAsync(GameVersion version);
    Task DeleteAsync(int id);
}
