using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Infrastructure.Downloading;

namespace ShoroCraftLauncher.Infrastructure.Services;

public class ModSearchService
{
    private readonly ISettingsRepository _settingsRepository;
    private readonly ILogger<ModSearchService> _logger;
    private readonly ILogService _logService;
    private readonly HttpClient _httpClient;

    public ModSearchService(ISettingsRepository settingsRepository, ILogger<ModSearchService> logger, ILogService logService, HttpClient httpClient)
    {
        _settingsRepository = settingsRepository;
        _logger = logger;
        _logService = logService;
        _httpClient = httpClient;
    }

    public async Task<List<Mod>> SearchModsAsync(string provider, string query, string minecraftVersion, string loaderType)
    {
        return provider.Equals("CurseForge", StringComparison.OrdinalIgnoreCase)
            ? await SearchCurseForgeAsync(query, minecraftVersion, loaderType).ConfigureAwait(false)
            : await SearchModrinthAsync(query, minecraftVersion, loaderType).ConfigureAwait(false);
    }

    public async Task<List<Mod>> SearchModrinthAsync(string query, string minecraftVersion, string loaderType)
    {
        var modrinthVersion = ToModrinthVersion(minecraftVersion);
        _logger.LogInformation("Searching Modrinth: {Query} for MC {Version} (Modrinth: {ModrinthVersion}) on {Loader}", query, minecraftVersion, modrinthVersion, loaderType);
        _logService.Info("ModSearch", "SearchModrinth", $"Buscando '{query}' en Modrinth para MC {minecraftVersion} ({loaderType})...");

        try
        {
            var url = $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(query)}&facets=[[\"versions:{modrinthVersion}\"],[\"categories:{loaderType.ToLower()}\"]]";
            _httpClient.DefaultRequestHeaders.UserAgent.Clear();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ShoroCraftLauncher/1.0.0");

            await ModrinthApiRateLimiter.WaitAsync().ConfigureAwait(false);
            var response = await _httpClient.GetAsync(url).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = System.Text.Json.JsonDocument.Parse(json);

            var results = new List<Mod>();
            foreach (var item in doc.RootElement.GetProperty("hits").EnumerateArray())
            {
                results.Add(new Mod
                {
                    Name = item.GetProperty("title").GetString() ?? "Unknown",
                    Description = item.GetProperty("description").GetString(),
                    IconPath = item.TryGetProperty("icon_url", out var icon) ? icon.GetString() : null,
                    FileName = item.GetProperty("project_id").GetString() ?? string.Empty,
                    ModVersion = (item.TryGetProperty("latest_version", out var v) ? v.GetString() : "latest") ?? "latest"
                });
            }
            _logService.Info("ModSearch", "SearchModrinth", $"Encontrados {results.Count} resultados.");
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Modrinth search failed");
            _logService.Error("ModService", "SearchModrinth", $"Error en búsqueda: {ex.Message}");
            return new List<Mod>();
        }
    }

    private async Task<List<Mod>> SearchCurseForgeAsync(string query, string minecraftVersion, string loaderType)
    {
        var apiKey = await _settingsRepository.GetAsync("curseforge_api_key").ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Configura una API key de CurseForge en Configuración.");

        var loaderTypeId = loaderType.ToLowerInvariant() switch
        {
            "forge" => 1,
            "neoforge" => 6,
            "fabric" => 4,
            "quilt" => 5,
            _ => 0
        };

        var url = "https://api.curseforge.com/v1/mods/search"
            + "?gameId=432"
            + "&classId=6"
            + $"&searchFilter={Uri.EscapeDataString(query)}"
            + $"&gameVersion={Uri.EscapeDataString(minecraftVersion)}"
            + $"&modLoaderType={loaderTypeId}"
            + "&sortField=2"
            + "&sortOrder=desc"
            + "&pageSize=20";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.UserAgent.ParseAdd("ShoroCraftLauncher/1.0.0");

        await CurseForgeApiRateLimiter.WaitAsync().ConfigureAwait(false);
        using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var results = new List<Mod>();

        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            var latest = item.TryGetProperty("latestFilesIndexes", out var indexes)
                ? indexes.EnumerateArray().FirstOrDefault()
                : default;

            results.Add(new Mod
            {
                Name = item.GetProperty("name").GetString() ?? "Unknown",
                Description = item.TryGetProperty("summary", out var summary) ? summary.GetString() : null,
                IconPath = item.TryGetProperty("logo", out var logo) && logo.TryGetProperty("thumbnailUrl", out var icon)
                    ? icon.GetString()
                    : null,
                FileName = item.GetProperty("id").GetInt32().ToString(),
                MinecraftVersion = latest.ValueKind == System.Text.Json.JsonValueKind.Object && latest.TryGetProperty("gameVersion", out var gameVersion)
                    ? gameVersion.GetString() ?? minecraftVersion
                    : minecraftVersion,
                ModVersion = latest.ValueKind == System.Text.Json.JsonValueKind.Object && latest.TryGetProperty("filename", out var filename)
                    ? filename.GetString() ?? "latest"
                    : "latest"
            });
        }

        return results;
    }

    private static string ToModrinthVersion(string mcVersion)
    {
        if (string.IsNullOrWhiteSpace(mcVersion)) return mcVersion;
        var trimmed = mcVersion.Trim();
        if (trimmed.StartsWith("26.", StringComparison.OrdinalIgnoreCase))
        {
            var parts = trimmed.Split('.');
            if (parts.Length >= 2 && int.TryParse(parts[1], out var minor))
            {
                return $"1.21.{minor}";
            }
        }
        return trimmed;
    }
}
