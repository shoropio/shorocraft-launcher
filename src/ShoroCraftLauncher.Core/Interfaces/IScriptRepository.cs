using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Core.Interfaces;

public interface IScriptRepository
{
    Task<List<Script>> GetByProfileIdAsync(int profileId);
    Task<Script?> GetByIdAsync(int id);
    Task<int> CreateAsync(Script script);
    Task UpdateAsync(Script script);
    Task DeleteAsync(int id);
}
