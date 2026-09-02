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
    #region Actualización de loaders

    public async Task<string?> CheckLoaderUpdateAsync(string loaderType, string mcVersion, string currentLoaderVersion)
    {
        if (string.IsNullOrEmpty(currentLoaderVersion) || currentLoaderVersion.Equals("latest", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            if (loaderType.Equals("fabric", StringComparison.OrdinalIgnoreCase))
            {
                if (!await FabricSupportsGameVersionAsync(mcVersion).ConfigureAwait(false))
                    return null;

                var json = await _httpClient.GetStringAsync(FabricLoaderVersionsUrl).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var latest = doc.RootElement[0].GetProperty("version").GetString();
                if (!string.IsNullOrEmpty(latest) && !latest.Equals(currentLoaderVersion, StringComparison.OrdinalIgnoreCase))
                    return latest;
            }
            else if (loaderType.Equals("quilt", StringComparison.OrdinalIgnoreCase))
            {
                var json = await _httpClient.GetStringAsync(QuiltInstallerVersionsUrl).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var latest = doc.RootElement[0].GetProperty("version").GetString();
                if (!string.IsNullOrEmpty(latest) && !latest.Equals(currentLoaderVersion, StringComparison.OrdinalIgnoreCase))
                    return latest;
            }
        }
        catch { }

        return null;
    }

    public async Task UpdateLoaderAsync(string mcVersion, string loaderType, string newLoaderVersion, string javaPath, string gameDir, Action<string>? onProgress = null)
    {
        onProgress?.Invoke($"Actualizando {loaderType} a {newLoaderVersion}...");
        await InstallLoaderAsync(mcVersion, loaderType, newLoaderVersion, javaPath, onProgress: onProgress, gameDir: gameDir).ConfigureAwait(false);
        onProgress?.Invoke($"{loaderType} actualizado a {newLoaderVersion}.");
    }

    #endregion

}
