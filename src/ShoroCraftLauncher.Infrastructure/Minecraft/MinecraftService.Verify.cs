using System.Diagnostics;
using System.IO.Compression;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Infrastructure.Downloading;

namespace ShoroCraftLauncher.Infrastructure.Minecraft;


public partial class MinecraftService : IMinecraftService
{
    #region Verificación y reparación

    public bool VerifyInstallationAsync(string gameDir)
    {
        gameDir = ResolveGameDirectory(gameDir);
        var versionsDir = Path.Combine(gameDir, "versions");
        if (!Directory.Exists(versionsDir) || !Directory.Exists(Path.Combine(gameDir, "libraries")))
            return false;

        return Directory.GetDirectories(versionsDir)
            .Select(Path.GetFileName)
            .Where(v => !string.IsNullOrEmpty(v))
            .Any(v => File.Exists(Path.Combine(versionsDir, v!, $"{v}.jar"))
                   && File.Exists(Path.Combine(versionsDir, v!, $"{v}.json"))
                   && File.Exists(Path.Combine(versionsDir, v!, ".shorocraft-installed.json")));
    }

    public async Task RepairInstallationAsync(string gameDir, IProgress<double>? progress = null)
    {
        gameDir = ResolveGameDirectory(gameDir);
        _logger.LogInformation("Repairing installation at {GameDir}", gameDir);
        foreach (var dir in new[] { "versions", "assets", "libraries", "mods", "resourcepacks", "shaderpacks", "saves", "cache", "logs", "natives" })
            Directory.CreateDirectory(Path.Combine(gameDir, dir));

        var versionsDir = Path.Combine(gameDir, "versions");
        if (!Directory.Exists(versionsDir)) return;

        foreach (var versionDir in Directory.GetDirectories(versionsDir))
        {
            var versionId = Path.GetFileName(versionDir);
            var markerPath = Path.Combine(versionDir, ".shorocraft-installed.json");
            if (!File.Exists(markerPath)) continue;

            try
            {
                using var versionData = await FetchVersionDataAsync(versionId).ConfigureAwait(false);
                if (versionData == null)
                {
                    _logService?.Warning("MinecraftRepair", "VersionDataMissing",
                        "No se pudo obtener datos de la versi�n para reparar.", new { versionId });
                    continue;
                }

                var jarPath = Path.Combine(versionDir, $"{versionId}.jar");
                if (!File.Exists(jarPath))
                {
                    var clientUrl = versionData.GetClientUrl();
                    if (clientUrl != null)
                        await DownloadFileAsync(clientUrl, jarPath, progress).ConfigureAwait(false);
                }

                var jsonPath = Path.Combine(versionDir, $"{versionId}.json");
                if (!File.Exists(jsonPath))
                    await File.WriteAllTextAsync(jsonPath, await _httpClient.GetStringAsync(versionData.Url)).ConfigureAwait(false);

                await DownloadLibrariesAsync(versionData, Path.Combine(gameDir, "libraries"), progress).ConfigureAwait(false);
                _logService?.Info("MinecraftRepair", "VersionRepaired", "Versión reparada correctamente.", new { versionId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to repair version {Version}", versionId);
                _logService?.Error("MinecraftRepair", "RepairFailed",
                    "No se pudo reparar la versión.", ex, new { versionId });
            }
        }
    }

    #endregion

}
