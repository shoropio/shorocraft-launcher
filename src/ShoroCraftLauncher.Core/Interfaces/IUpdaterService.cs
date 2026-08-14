namespace ShoroCraftLauncher.Core.Interfaces;

public interface IUpdaterService
{
    Task<(bool IsUpdateAvailable, string? LatestVersion, string? DownloadUrl, string? Sha256)> CheckForUpdatesAsync(string currentVersion);
    Task<string?> DownloadUpdateAsync(string downloadUrl, string version, string? expectedSha256 = null);
    Task LaunchInstallerAsync(string installerPath);
}
