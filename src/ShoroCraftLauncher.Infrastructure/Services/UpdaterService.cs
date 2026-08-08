using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ShoroCraftLauncher.Core.Interfaces;

namespace ShoroCraftLauncher.Infrastructure.Services;

public class UpdaterService : IUpdaterService
{
    private readonly HttpClient _httpClient;

    public UpdaterService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<(bool IsUpdateAvailable, string? LatestVersion, string? DownloadUrl)> CheckForUpdatesAsync(string currentVersion)
    {
        try
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Clear();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ShoroCraftLauncher/1.0");
            
            var response = await _httpClient.GetAsync("https://api.github.com/repos/shoropio/shorocraft-launcher/releases/latest");
            if (!response.IsSuccessStatusCode) return (false, null, null);

            var release = await response.Content.ReadFromJsonAsync<GitHubRelease>();
            if (release == null || string.IsNullOrEmpty(release.TagName)) return (false, null, null);

            var latestVersion = release.TagName.Replace("v", "");
            currentVersion = currentVersion.Replace("v", "");

            if (Version.TryParse(latestVersion, out var latest) && Version.TryParse(currentVersion, out var current))
            {
                if (latest > current)
                {
                    var exeAsset = release.Assets?.FirstOrDefault(a => a.Name.EndsWith(".exe"));
                    return (true, release.TagName, exeAsset?.BrowserDownloadUrl);
                }
            }
        }
        catch { }
        
        return (false, null, null);
    }

    public async Task<string?> DownloadUpdateAsync(string downloadUrl, string version)
    {
        try
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Clear();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ShoroCraftLauncher/1.0");

            var updatesDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ShoroCraftLauncher", "updates");
            Directory.CreateDirectory(updatesDir);

            var safeVersion = string.IsNullOrWhiteSpace(version) ? "latest" : version.TrimStart('v');
            var fileName = $"ShoroCraftLauncher_Setup_{safeVersion}.exe";
            var filePath = Path.Combine(updatesDir, fileName);

            if (File.Exists(filePath))
                return filePath;

            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return null;

            await using var input = await response.Content.ReadAsStreamAsync();
            await using var output = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output);

            return filePath;
        }
        catch
        {
            return null;
        }
    }

    public Task LaunchInstallerAsync(string installerPath)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installerPath) { UseShellExecute = true });
        return Task.CompletedTask;
    }
}

public class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }
    
    [JsonPropertyName("assets")]
    public List<GitHubAsset>? Assets { get; set; }
}

public class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }
}
