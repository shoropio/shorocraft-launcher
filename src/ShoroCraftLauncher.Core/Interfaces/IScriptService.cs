using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Core.Interfaces;

public interface IScriptService
{
    Task<List<Script>> GetScriptsAsync(int profileId);
    Task<Script> ImportScriptAsync(int profileId, string sourceFilePath);
    Task<string> ReadScriptContentAsync(int scriptId);
    Task SaveScriptContentAsync(int scriptId, string content);
    Task DeleteScriptAsync(int scriptId);
    Task<string> CreateBackupAsync(int profileId, string filePath);
}
