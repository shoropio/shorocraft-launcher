using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Core.Interfaces;

public interface IModRepository
{
    Task<List<Mod>> GetByProfileIdAsync(int profileId);
    Task<Mod?> GetByIdAsync(int id);
    Task<int> CreateAsync(Mod mod);
    Task UpdateAsync(Mod mod);
    Task DeleteAsync(int id);
    Task DeleteByProfileIdAsync(int profileId);
}
