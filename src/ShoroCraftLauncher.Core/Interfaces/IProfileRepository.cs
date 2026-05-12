using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Core.Interfaces;

public interface IProfileRepository
{
    Task<List<Profile>> GetAllAsync();
    Task<Profile?> GetByIdAsync(int id);
    Task<int> CreateAsync(Profile profile);
    Task UpdateAsync(Profile profile);
    Task DeleteAsync(int id);
}
