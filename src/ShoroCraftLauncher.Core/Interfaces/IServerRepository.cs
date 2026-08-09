using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Core.Interfaces;

public interface IServerRepository
{
    Task<List<MinecraftServer>> GetAllAsync();
    Task<MinecraftServer?> GetByIdAsync(int id);
    Task<int> CreateAsync(MinecraftServer server);
    Task UpdateAsync(MinecraftServer server);
    Task DeleteAsync(int id);
}
