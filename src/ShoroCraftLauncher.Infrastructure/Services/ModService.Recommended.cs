using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.Core;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Infrastructure.Downloading;

namespace ShoroCraftLauncher.Infrastructure.Services;


public partial class ModService
{
    public async Task<List<Mod>> GetRecommendedModsAsync()
    {
        _logService.Info("ModService", "GetRecommended", "Cargando mods recomendados...");
        var results = new List<Mod>();

        foreach (var projectId in RecommendedModProjectIds)
        {
            try
            {
                using var request = CreateModrinthRequest($"https://api.modrinth.com/v2/project/{projectId}");
                await ModrinthApiRateLimiter.WaitAsync().ConfigureAwait(false);
                var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                var root = doc.RootElement;
                results.Add(new Mod
                {
                    Name = root.TryGetProperty("title", out var t) ? t.GetString() ?? projectId : projectId,
                    Description = root.TryGetProperty("description", out var d) ? d.GetString() : null,
                    IconPath = root.TryGetProperty("icon_url", out var i) ? i.GetString() : null,
                    FileName = projectId,
                    ModVersion = (root.TryGetProperty("latest_version", out var v) ? v.GetString() : "latest") ?? "latest"
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load recommended mod {ProjectId}", projectId);
            }
        }

        _logService.Info("ModService", "GetRecommended", $"Cargados {results.Count} mods recomendados.");
        return results;
    }

    private static readonly string[] RecommendedModProjectIds =
    {
        "P7dR8mSH", // Fabric API
        "YL57xq9U", // Iris Shaders
        "AANobbMI", // Sodium
        "Nv2fQJo5", // ReplayMod (grabador de partidas)
        "mOgUt4GM", // Mod Menu
        "PtjYWJkn", // Sodium Extra
        "hvFnDODi", // LazyDFU,
    };

}
