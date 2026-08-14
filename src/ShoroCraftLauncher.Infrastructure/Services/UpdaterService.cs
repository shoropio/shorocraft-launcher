using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Infrastructure.Downloading;

namespace ShoroCraftLauncher.Infrastructure.Services;

public class UpdaterService : IUpdaterService
{
    private readonly HttpClient _httpClient;
    private readonly IResumableDownloadService _resumableDownloadService;
    private readonly ILogger<UpdaterService> _logger;

    public UpdaterService(HttpClient httpClient, ILogger<UpdaterService> logger, IResumableDownloadService? resumableDownloadService = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _resumableDownloadService = resumableDownloadService ?? new ResumableDownloadService(httpClient);
    }

    public async Task<(bool IsUpdateAvailable, string? LatestVersion, string? DownloadUrl, string? Sha256)> CheckForUpdatesAsync(string currentVersion)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/shoropio/shorocraft-launcher/releases/latest");
            request.Headers.UserAgent.ParseAdd("ShoroCraftLauncher/1.0");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Update check failed with status {Status}", response.StatusCode);
                return (false, null, null, null);
            }

            var release = await response.Content.ReadFromJsonAsync<GitHubRelease>();
            if (release == null || string.IsNullOrEmpty(release.TagName)) return (false, null, null, null);

            var latestVersion = release.TagName.Replace("v", "");
            currentVersion = currentVersion.Replace("v", "");

            if (Version.TryParse(latestVersion, out var latest) && Version.TryParse(currentVersion, out var current))
            {
                if (latest > current)
                {
                    var exeAsset = release.Assets?.FirstOrDefault(a => a.Name.EndsWith(".exe"));
                    return (true, release.TagName, exeAsset?.BrowserDownloadUrl, exeAsset?.Digest);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check for updates");
        }

        return (false, null, null, null);
    }

    public async Task<string?> DownloadUpdateAsync(string downloadUrl, string version, string? expectedSha256 = null)
    {
        try
        {
            var updatesDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ShoroCraftLauncher", "updates");
            Directory.CreateDirectory(updatesDir);

            var safeVersion = string.IsNullOrWhiteSpace(version) ? "latest" : version.TrimStart('v');
            var fileName = $"ShoroCraftLauncher_Setup_{safeVersion}.exe";
            var filePath = Path.Combine(updatesDir, fileName);

            if (File.Exists(filePath))
            {
                if (MatchesExpectedHash(filePath, expectedSha256))
                    return filePath;
                File.Delete(filePath);
            }

            await _resumableDownloadService.DownloadAsync(downloadUrl, filePath);

            if (!MatchesExpectedHash(filePath, expectedSha256))
            {
                _logger.LogError("Downloaded update does not match expected SHA-256 digest");
                try { File.Delete(filePath); } catch { }
                return null;
            }

            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download update from {Url}", downloadUrl);
            return null;
        }
    }

    private static bool MatchesExpectedHash(string filePath, string? expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256)) return true;

        using var stream = File.OpenRead(filePath);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();

        // GitHub devuelve el digest como "sha256:<hex>"
        var expected = expectedSha256.Trim();
        if (expected.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            expected = expected["sha256:".Length..];

        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
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

    [JsonPropertyName("digest")]
    public string? Digest { get; set; }
}
