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
