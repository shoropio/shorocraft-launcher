namespace ShoroCraftLauncher.Core.Interfaces;

public interface IUpdaterService
{
    Task<(bool IsUpdateAvailable, string? LatestVersion, string? DownloadUrl)> CheckForUpdatesAsync(string currentVersion);
    Task<string?> DownloadUpdateAsync(string downloadUrl, string version);
    Task LaunchInstallerAsync(string installerPath);
}
