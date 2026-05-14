using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Core.Interfaces;

public interface IMinecraftService
{
    Task<List<GameVersion>> FetchAvailableVersionsAsync();
    Task InstallVersionAsync(string versionId, IProgress<double>? progress = null);
    Task InstallLoaderAsync(string versionId, string loaderType, string loaderVersion, string javaPath, Action<string>? onProgress = null, IProgress<double>? progress = null, Action<string>? onLog = null);
    Task<string> ResolveLatestLoaderVersionAsync(string loaderType, string mcVersion);
    Task<bool> VerifyInstallationAsync(string gameDir);
    Task RepairInstallationAsync(string gameDir, IProgress<double>? progress = null);
    Task<System.Diagnostics.Process> LaunchGameAsync(Profile profile, string gameDir, string javaPath, string accessToken, string uuid, string username, Action<double, string>? onProgress = null);
    Task<string> ResolveVersionIdAsync(string versionId);
    string GetDefaultGameDirectory(string profileName);
    string GetModsDirectory(string gameDir);
    string GetResourcePacksDirectory(string gameDir);
    string GetShaderPacksDirectory(string gameDir);
}
