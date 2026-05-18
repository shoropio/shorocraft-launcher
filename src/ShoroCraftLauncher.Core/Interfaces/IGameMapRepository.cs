using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Core.Interfaces;

public interface IGameMapRepository
{
    Task<List<GameMap>> GetByProfileIdAsync(int profileId);
    Task<GameMap?> GetByIdAsync(int id);
    Task<int> CreateAsync(GameMap map);
    Task UpdateAsync(GameMap map);
    Task DeleteAsync(int id);
}
