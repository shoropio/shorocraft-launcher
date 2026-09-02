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
    #region Detalles internos de instalación

    private string BuildClassPath(string globalDir, string gameDir, string versionId)
    {
        var entries = new List<string>();

        var jarPath = Path.Combine(globalDir, "versions", versionId, $"{versionId}.jar");
        if (File.Exists(jarPath)) entries.Add(jarPath);

        var libsDir = Path.Combine(globalDir, "libraries");
        if (Directory.Exists(libsDir))
        {
            foreach (var lib in Directory.GetFiles(libsDir, "*.jar", SearchOption.AllDirectories))
                entries.Add(lib);
        }

        var modsDir = Path.Combine(gameDir, "mods");
        if (Directory.Exists(modsDir))
        {
            foreach (var mod in Directory.GetFiles(modsDir, "*.jar"))
                entries.Add(mod);
        }

        return string.Join(";", entries);
    }

    private async Task<VersionData?> FetchVersionDataAsync(string versionId)
    {
        try
        {
            var manifestJson = await _httpClient.GetStringAsync(VersionManifestUrl).ConfigureAwait(false);
            using var manifest = JsonDocument.Parse(manifestJson);
            string? versionUrl = null;

            foreach (var v in manifest.RootElement.GetProperty("versions").EnumerateArray())
            {
                if (v.GetProperty("id").GetString() == versionId)
                {
                    versionUrl = v.GetProperty("url").GetString();
                    break;
                }
            }

            if (versionUrl == null) return null;

            var versionJson = await _httpClient.GetStringAsync(versionUrl).ConfigureAwait(false);
            return new VersionData(versionId, versionUrl, versionJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch version data for {Version}", versionId);
            return null;
        }
    }

    private async Task<int> DownloadLibrariesAsync(VersionData versionData, string libsDir, IProgress<double>? progress)
    {
        int count = 0;
        var libs = versionData.GetLibraries();
        var failed = new List<string>();
        for (int i = 0; i < libs.Count; i++)
        {
            var lib = libs[i];
            if (lib.Path == null || lib.Url == null) continue;

            var destPath = Path.Combine(libsDir, lib.Path);
            if (File.Exists(destPath)) continue;

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            try
            {
                await DownloadFileAsync(lib.Url, destPath, null).ConfigureAwait(false);
                count++;
                progress?.Report((double)(i + 1) / libs.Count * 100);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download library {Lib}", lib.Path);
                failed.Add(lib.Path);
            }
        }

        if (failed.Count > 0)
        {
            var detail = string.Join(Environment.NewLine, failed.Take(10));
            if (failed.Count > 10) detail += Environment.NewLine + "... y " + (failed.Count - 10) + " más";
            throw new Exception($"No se pudieron descargar {failed.Count} librerías de Minecraft (verifica tu conexión):{Environment.NewLine}{detail}");
        }

        return count;
    }

    private async Task DownloadFileAsync(string url, string destinationPath, IProgress<double>? progress)
        => await _resumableDownloadService.DownloadAsync(url, destinationPath, progress).ConfigureAwait(false);

    private static string GetMinecraftGameDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");

    private static string ResolveGameDirectory(string? gameDir) =>
        string.IsNullOrWhiteSpace(gameDir) ? GetMinecraftGameDir() : gameDir;

    private static bool IsVersionComplete(string versionsDir, string versionId)
    {
        return Directory.Exists(versionsDir)
            && File.Exists(Path.Combine(versionsDir, $"{versionId}.jar"))
            && File.Exists(Path.Combine(versionsDir, $"{versionId}.json"))
            && File.Exists(Path.Combine(versionsDir, ".shorocraft-installed.json"));
    }

    private static async Task EnsureLauncherProfileAsync(string gameDir, string versionId)
    {
        Directory.CreateDirectory(gameDir);

        var profilesPath = Path.Combine(gameDir, "launcher_profiles.json");
        JsonObject root;

        if (File.Exists(profilesPath))
        {
            try
            {
                root = JsonNode.Parse(await File.ReadAllTextAsync(profilesPath).ConfigureAwait(false))?.AsObject() ?? new JsonObject();
            }
            catch
            {
                root = new JsonObject();
            }
        }
        else
        {
            root = new JsonObject();
        }

        var profiles = root["profiles"] as JsonObject ?? new JsonObject();
        root["profiles"] = profiles;

        var now = DateTimeOffset.UtcNow.ToString("O");
        var profile = profiles["ShoroCraft"] as JsonObject ?? new JsonObject();
        profile["name"] = "ShoroCraft";
        profile["type"] = "custom";
        profile["created"] ??= now;
        profile["lastUsed"] = now;
        profile["lastVersionId"] = versionId;
        profiles["ShoroCraft"] = profile;

        root["selectedProfile"] = "ShoroCraft";
        root["clientToken"] ??= Guid.NewGuid().ToString("N");
        root["authenticationDatabase"] ??= new JsonObject();
        root["settings"] ??= new JsonObject();
        root["version"] ??= 3;
        root["launcherVersion"] ??= new JsonObject
        {
            ["name"] = "ShoroCraft Launcher",
            ["format"] = 21
        };

        await File.WriteAllTextAsync(
            profilesPath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
    }

    private static string SanitizeFolderName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    private async Task ExtractNativesAsync(VersionData versionData, string gameDir, string nativesDir)
    {
        _logger.LogInformation("Extracting native libraries...");

        var nativeCacheDir = Path.Combine(gameDir, "cache", "natives");
        Directory.CreateDirectory(nativeCacheDir);
        var osName = GetCurrentOsName();
        var nativeEntries = versionData.GetNativeLibraries(osName);

        if (nativeEntries.Count == 0)
        {
            _logger.LogWarning("No native libraries found for OS {Os}", osName);
            return;
        }

        foreach (var native in nativeEntries)
        {
            var jarName = Path.GetFileName(native.Path);
            var destPath = Path.Combine(nativeCacheDir, jarName);
            if (!File.Exists(destPath))
            {
                _logger.LogInformation("Downloading native library: {Jar}", jarName);
                await DownloadFileAsync(native.Url, destPath, null).ConfigureAwait(false);
            }

            _logger.LogDebug("Extracting natives from {Jar}", jarName);
            try
            {
                using var archive = ZipFile.OpenRead(destPath);
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                        || entry.FullName.EndsWith(".so", StringComparison.OrdinalIgnoreCase)
                        || entry.FullName.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase)
                        || entry.FullName.EndsWith(".jnilib", StringComparison.OrdinalIgnoreCase))
                    {
                        var extractPath = Path.Combine(nativesDir, Path.GetFileName(entry.FullName));
                        if (!File.Exists(extractPath))
                        {
                            entry.ExtractToFile(extractPath, overwrite: true);
                            _logger.LogTrace("Extracted native: {File}", entry.FullName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract natives from {Jar}", jarName);
            }
        }

        _logger.LogInformation("Native extraction complete");
    }

    private async Task DownloadAssetsAsync(VersionData versionData, string gameDir)
    {
        var assetIndexUrl = versionData.GetAssetIndexUrl();
        var assetIndexId = versionData.GetAssetIndexId();
        if (string.IsNullOrEmpty(assetIndexUrl))
        {
            _logger.LogWarning("No asset index URL for version {Version}", versionData.Id);
            return;
        }

        var indexesDir = Path.Combine(gameDir, "assets", "indexes");
        Directory.CreateDirectory(indexesDir);
        var indexPath = Path.Combine(indexesDir, $"{assetIndexId}.json");

        if (!File.Exists(indexPath))
        {
            _logger.LogInformation("Downloading asset index {AssetIndexId}...", assetIndexId);
            var indexJson = await _httpClient.GetStringAsync(assetIndexUrl).ConfigureAwait(false);
            await File.WriteAllTextAsync(indexPath, indexJson).ConfigureAwait(false);
        }

        var assetsDir = Path.Combine(gameDir, "assets", "objects");
        Directory.CreateDirectory(assetsDir);

        using var indexDoc = JsonDocument.Parse(await File.ReadAllTextAsync(indexPath).ConfigureAwait(false));
        if (!indexDoc.RootElement.TryGetProperty("objects", out var objects)) return;

        var total = objects.EnumerateObject().Count();
        var count = 0;
        foreach (var obj in objects.EnumerateObject())
        {
            var hash = obj.Value.GetProperty("hash").GetString();
            if (string.IsNullOrEmpty(hash)) continue;

            var hashPrefix = hash[..2];
            var objectDir = Path.Combine(assetsDir, hashPrefix);
            Directory.CreateDirectory(objectDir);
            var objectPath = Path.Combine(objectDir, hash);

            if (!File.Exists(objectPath))
            {
                var assetUrl = $"https://resources.download.minecraft.net/{hashPrefix}/{hash}";
                try
                {
                    await DownloadFileAsync(assetUrl, objectPath, null).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to download asset {Hash}", hash);
                }
            }

            count++;
            if (count % 100 == 0)
                _logger.LogDebug("Downloaded {Count}/{Total} assets", count, total);
        }

        _logger.LogInformation("Asset download complete: {Count} assets", count);
    }

    private static string GetCurrentOsName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "osx";
        return "linux";
    }

    #endregion

}
