using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Core.Interfaces;

public interface IMinecraftService
{
    Task<List<GameVersion>> FetchAvailableVersionsAsync();
    Task InstallVersionAsync(string versionId, IProgress<double>? progress = null, string? gameDir = null);
    Task InstallLoaderAsync(string versionId, string loaderType, string loaderVersion, string javaPath, Action<string>? onProgress = null, IProgress<double>? progress = null, Action<string>? onLog = null, string? gameDir = null);
    Task<string> ResolveLatestLoaderVersionAsync(string loaderType, string mcVersion);
    bool VerifyInstallationAsync(string gameDir);
    Task RepairInstallationAsync(string gameDir, IProgress<double>? progress = null);
    Task<System.Diagnostics.Process> LaunchGameAsync(Profile profile, string gameDir, string javaPath, string accessToken, string uuid, string username, Action<double, string>? onProgress = null);
    Task<string> ResolveVersionIdAsync(string versionId);
    Task<string?> GetServerJarUrlAsync(string versionId);
    string GetDefaultGameDirectory(string profileName);
    string SanitizeProfileFolderName(string profileName);
    string GetModsDirectory(string gameDir);
    string GetResourcePacksDirectory(string gameDir);
    string GetShaderPacksDirectory(string gameDir);
    string GetSavesDirectory(string gameDir);
    Task<string?> CheckLoaderUpdateAsync(string loaderType, string mcVersion, string currentLoaderVersion);
    Task UpdateLoaderAsync(string mcVersion, string loaderType, string newLoaderVersion, string javaPath, string gameDir, Action<string>? onProgress = null);
}
