using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Core.Interfaces;

public interface IGameMapService
{
    Task<List<GameMap>> GetMapsAsync(int profileId);
    Task<GameMap> AddMapAsync(int profileId, string sourceFilePath);
    Task RemoveMapAsync(GameMap map);
    Task<string> GetMapsFolderAsync(int profileId);
}
