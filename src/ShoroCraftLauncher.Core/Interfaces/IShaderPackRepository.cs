using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Core.Interfaces;

public interface IShaderPackRepository
{
    Task<List<ShaderPack>> GetByProfileIdAsync(int profileId);
    Task<ShaderPack?> GetByIdAsync(int id);
    Task<int> CreateAsync(ShaderPack pack);
    Task UpdateAsync(ShaderPack pack);
    Task DeleteAsync(int id);
}
