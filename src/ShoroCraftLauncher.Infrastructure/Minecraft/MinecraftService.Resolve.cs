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
    #region Resolución de versiones y loaders

    public async Task<string> ResolveVersionIdAsync(string versionId)
    {
        if (versionId.ToLower() != "latest") return versionId;
        try
        {
            var json = await _httpClient.GetStringAsync(VersionManifestUrl).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            foreach (var v in doc.RootElement.GetProperty("versions").EnumerateArray())
            {
                if (v.GetProperty("type").GetString() == "release")
                    return v.GetProperty("id").GetString() ?? "1.21";
            }
        }
        catch { }
        return "1.21";
    }

    public async Task<string?> GetServerJarUrlAsync(string versionId)
    {
        try
        {
            var resolved = versionId.ToLower() == "latest" ? await ResolveVersionIdAsync("latest").ConfigureAwait(false) : versionId;
            using var versionData = await FetchVersionDataAsync(resolved).ConfigureAwait(false);
            return versionData?.GetServerUrl();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve server jar URL for {Version}", versionId);
            return null;
        }
    }

    public async Task<string> ResolveLatestLoaderVersionAsync(string loaderType, string mcVersion)
    {
        try
        {
            return loaderType.ToLower() switch
            {
                "forge" => await ResolveLatestForgeVersionAsync(mcVersion).ConfigureAwait(false),
                "neoforge" => await ResolveLatestNeoForgeVersionAsync(mcVersion).ConfigureAwait(false),
                "fabric" => await ResolveLatestFabricLoaderVersionAsync(mcVersion).ConfigureAwait(false),
                "quilt" => await ResolveLatestQuiltInstallerVersionAsync(mcVersion).ConfigureAwait(false),
                _ => "latest"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve latest {Loader} version for MC {McVersion}", loaderType, mcVersion);
            return "latest";
        }
    }

    private async Task<string> ResolveLatestForgeVersionAsync(string mcVersion)
    {
        var json = await _httpClient.GetStringAsync(ForgePromotionsUrl).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var promos = doc.RootElement.GetProperty("promos");

        if (promos.TryGetProperty($"{mcVersion}-recommended", out var rec))
            return rec.GetString() ?? "latest";

        if (promos.TryGetProperty($"{mcVersion}-latest", out var lat))
            return lat.GetString() ?? "latest";

        _logger.LogWarning("No Forge version found for MC {McVersion}, falling back to 'latest'", mcVersion);
        return "latest";
    }

    private async Task<string> ResolveLatestNeoForgeVersionAsync(string mcVersion)
    {
        var xml = await _httpClient.GetStringAsync(NeoForgeMetadataUrl).ConfigureAwait(false);
        var doc = System.Xml.Linq.XDocument.Parse(xml);
        var versions = doc.Root?
            .Element("versioning")?
            .Element("versions")?
            .Elements("version")
            .Select(v => v.Value)
            .Where(v => v.StartsWith($"{mcVersion.Substring(2)}.", StringComparison.OrdinalIgnoreCase))
            .Select(v => new { Raw = v, Parsed = Version.TryParse(v, out var ver) ? ver : new Version(0, 0, 0) })
            .OrderBy(x => x.Parsed)
            .ToList();

        if (versions == null || versions.Count == 0)
        {
            _logger.LogWarning("No NeoForge version found for MC {McVersion}, falling back to 'latest'", mcVersion);
            return "latest";
        }

        return versions.Last().Raw;
    }

    private async Task<string> ResolveLatestFabricLoaderVersionAsync(string mcVersion)
    {
        if (!await FabricSupportsGameVersionAsync(mcVersion).ConfigureAwait(false))
            throw new Exception($"Fabric no reporta soporte para Minecraft {mcVersion}.");

        var json = await _httpClient.GetStringAsync(FabricLoaderVersionsUrl).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement[0].GetProperty("version").GetString() ?? "latest";
    }

    private async Task<string> ResolveLatestFabricInstallerVersionAsync()
    {
        var json = await _httpClient.GetStringAsync(FabricInstallerVersionsUrl).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement[0].GetProperty("version").GetString() ?? "latest";
    }

    private async Task<string> ResolveLatestQuiltInstallerVersionAsync(string mcVersion)
    {
        if (!await QuiltSupportsGameVersionAsync(mcVersion).ConfigureAwait(false))
            throw new Exception($"Quilt no reporta soporte para Minecraft {mcVersion}.");

        var json = await _httpClient.GetStringAsync(QuiltInstallerVersionsUrl).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement[0].GetProperty("version").GetString() ?? "latest";
    }

    private async Task<bool> FabricSupportsGameVersionAsync(string mcVersion)
    {
        var json = await _httpClient.GetStringAsync(FabricGameVersionsUrl).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray().Any(v =>
            string.Equals(v.GetProperty("version").GetString(), mcVersion, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> QuiltSupportsGameVersionAsync(string mcVersion)
    {
        var json = await _httpClient.GetStringAsync(QuiltGameVersionsUrl).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray().Any(v =>
            string.Equals(v.GetProperty("version").GetString(), mcVersion, StringComparison.OrdinalIgnoreCase));
    }

    #endregion

}
