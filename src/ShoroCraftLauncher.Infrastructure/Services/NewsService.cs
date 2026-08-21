using System.Net.Http.Json;
using System.Text.Json;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Infrastructure.Services;

public class NewsService : INewsService
{
    private readonly HttpClient _httpClient;
    private readonly ILogService _logService;

    public NewsService(HttpClient httpClient, ILogService logService)
    {
        _httpClient = httpClient;
        _logService = logService;
    }

    public async Task<List<NewsItem>> GetNewsAsync()
    {
        var items = new List<NewsItem>();

        try
        {
            var statusItems = await FetchMojangStatusAsync().ConfigureAwait(false);
            items.AddRange(statusItems);
        }
        catch (Exception ex)
        {
            _logService.Warning("NewsService", "GetNews", $"Error fetching Mojang status: {ex.Message}");
        }

        try
        {
            var versionItems = await FetchVersionNewsAsync().ConfigureAwait(false);
            items.AddRange(versionItems);
        }
        catch (Exception ex)
        {
            _logService.Warning("NewsService", "GetNews", $"Error fetching version news: {ex.Message}");
        }

        return items.OrderByDescending(n => n.Date).Take(10).ToList();
    }

    private async Task<List<NewsItem>> FetchMojangStatusAsync()
    {
        var items = new List<NewsItem>();

        var response = await _httpClient.GetAsync("https://status.mojang.com/api/v2/components").ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("components", out var components))
            return items;

        foreach (var component in components.EnumerateArray())
        {
            if (!component.TryGetProperty("name", out var nameProp) ||
                !component.TryGetProperty("status", out var statusProp))
                continue;

            var name = nameProp.GetString() ?? "";
            var status = statusProp.GetString() ?? "";

            if (status == "none") continue;

            var statusLabel = status switch
            {
                "minor" => "Degradado",
                "major" => "Caído",
                "partial" => "Parcial",
                "disrupted" => "Interrumpido",
                _ => status
            };

            items.Add(new NewsItem
            {
                Title = $"{name}: {statusLabel}",
                Summary = $"Estado del servicio de {name}.",
                Url = "https://status.mojang.com",
                Date = DateTime.UtcNow,
                Category = "Servidor"
            });
        }

        return items;
    }

    private async Task<List<NewsItem>> FetchVersionNewsAsync()
    {
        var items = new List<NewsItem>();

        var response = await _httpClient.GetAsync("https://launchermeta.mojang.com/mc/game/version_manifest_v2.json").ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var manifest = await response.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(false);

        if (!manifest.TryGetProperty("latest", out var latest))
            return items;

        var latestRelease = latest.TryGetProperty("release", out var rel) ? rel.GetString() ?? "" : "";
        var latestSnapshot = latest.TryGetProperty("snapshot", out var snap) ? snap.GetString() ?? "" : "";

        if (!string.IsNullOrEmpty(latestRelease))
        {
            items.Add(new NewsItem
            {
                Title = $"Minecraft {latestRelease} disponible",
                Summary = "Última versión estable de Minecraft Java Edition.",
                Url = "https://www.minecraft.net/en-us/store/minecraft-java-edition",
                Date = DateTime.UtcNow,
                Category = "Actualización"
            });
        }

        if (!string.IsNullOrEmpty(latestSnapshot))
        {
            items.Add(new NewsItem
            {
                Title = $"Snapshot: {latestSnapshot}",
                Summary = "Última versión snapshot disponible para pruebas.",
                Url = "https://www.minecraft.net/en-us/about-games/minecraft-snapshots",
                Date = DateTime.UtcNow.AddDays(-1),
                Category = "Snapshot"
            });
        }

        return items;
    }
}
