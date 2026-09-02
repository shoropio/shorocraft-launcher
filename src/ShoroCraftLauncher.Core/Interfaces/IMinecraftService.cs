using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Core.Interfaces;

/// <summary>Rutas y directorios de juego del launcher.</summary>
public interface IGameDirectories
{
    string GetDefaultGameDirectory(string profileName);
    string SanitizeProfileFolderName(string profileName);
    string GetModsDirectory(string gameDir);
    string GetResourcePacksDirectory(string gameDir);
    string GetShaderPacksDirectory(string gameDir);
    string GetSavesDirectory(string gameDir);
}

/// <summary>Catálogo y resolución de versiones de Minecraft.</summary>
public interface IGameVersionCatalog
{
    Task<List<GameVersion>> FetchAvailableVersionsAsync();
    Task<string> ResolveVersionIdAsync(string versionId);
    Task<string?> GetServerJarUrlAsync(string versionId);
}

/// <summary>Instalación y reparación de versiones de Minecraft y loaders.</summary>
public interface IGameInstaller
{
    Task InstallVersionAsync(string versionId, IProgress<double>? progress = null, string? gameDir = null);
    Task InstallLoaderAsync(string versionId, string loaderType, string loaderVersion, string javaPath, Action<string>? onProgress = null, IProgress<double>? progress = null, Action<string>? onLog = null, string? gameDir = null);
    bool VerifyInstallationAsync(string gameDir);
    Task RepairInstallationAsync(string gameDir, IProgress<double>? progress = null);
}

/// <summary>Lanzamiento del proceso de juego.</summary>
public interface IGameLauncher
{
    Task<IGameProcess> LaunchGameAsync(Profile profile, string gameDir, string javaPath, string accessToken, string uuid, string username, Action<double, string>? onProgress = null);
}

/// <summary>Resolución y actualización de loaders (Fabric, Forge, NeoForge, Quilt).</summary>
public interface ILoaderVersionService
{
    Task<string> ResolveLatestLoaderVersionAsync(string loaderType, string mcVersion);
    Task<string?> CheckLoaderUpdateAsync(string loaderType, string mcVersion, string currentLoaderVersion);
    Task PreDownloadLoaderInstallerAsync(string versionId, string loaderType, string loaderVersion, string gameDir, IProgress<double>? progress = null);
    Task UpdateLoaderAsync(string mcVersion, string loaderType, string newLoaderVersion, string javaPath, string gameDir, Action<string>? onProgress = null);
}

/// <summary>
/// Fachada compuesta de todos los servicios de Minecraft.
/// Los consumidores deberían depender de las interfaces segregadas.
/// </summary>
public interface IMinecraftService : IGameDirectories, IGameVersionCatalog, IGameInstaller, IGameLauncher, ILoaderVersionService
{
}
