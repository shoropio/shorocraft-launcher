using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Core.Interfaces;

public interface IShaderPackService
{
    Task<List<ShaderPack>> GetPacksAsync(int profileId);
    Task<ShaderPack> AddPackAsync(int profileId, string sourceFilePath);
    Task TogglePackAsync(int packId);
    Task RemovePackAsync(int packId);
    Task<string> GetPacksFolderAsync(int profileId);
    Task<bool> HasShaderSupportAsync(int profileId);
}
