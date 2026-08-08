using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Core.Interfaces;

public interface IModpackService
{
    Task<ModpackImportResult> ImportFromFileAsync(int profileId, string mrpackPath, Action<string>? onProgress = null);
    Task<ModpackImportResult> ImportFromUrlAsync(int profileId, string url, Action<string>? onProgress = null);
}
