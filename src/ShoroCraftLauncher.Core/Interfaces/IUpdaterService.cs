namespace ShoroCraftLauncher.Core.Interfaces;

public interface IUpdaterService
{
    Task<(bool IsUpdateAvailable, string? LatestVersion, string? DownloadUrl)> CheckForUpdatesAsync(string currentVersion);
}
