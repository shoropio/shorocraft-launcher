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
    #region Campos y configuración

    private readonly ILogger<MinecraftService> _logger;
    private readonly HttpClient _httpClient;
    private readonly ILogService? _logService;
    private readonly IResumableDownloadService _resumableDownloadService;
    private const string VersionManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private const string ForgePromotionsUrl = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
    private const string FabricGameVersionsUrl = "https://meta.fabricmc.net/v2/versions/game";
    private const string FabricLoaderVersionsUrl = "https://meta.fabricmc.net/v2/versions/loader";
    private const string FabricInstallerVersionsUrl = "https://meta.fabricmc.net/v2/versions/installer";
    private const string QuiltGameVersionsUrl = "https://meta.quiltmc.org/v3/versions/game";
    private const string QuiltInstallerVersionsUrl = "https://meta.quiltmc.org/v3/versions/installer";
    private const string NeoForgeMetadataUrl = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";
    private const int MaxInstallAttempts = 3;
    private static readonly TimeSpan InstallAttemptTimeout = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CmlHttpTimeout = TimeSpan.FromMinutes(5);

    #endregion

    #region Constructor

    public MinecraftService(ILogger<MinecraftService> logger, HttpClient httpClient, ILogService? logService = null,
        IResumableDownloadService? resumableDownloadService = null)
    {
        _logger = logger;
        _httpClient = httpClient;
        _logService = logService;
        _resumableDownloadService = resumableDownloadService ?? new ResumableDownloadService(httpClient);
    }

    #endregion

    #region Rutas y directorios públicos

    public string GetDefaultGameDirectory(string profileName)
    {
        var safeName = SanitizeFolderName(profileName);
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".minecraft", safeName);
    }

    public string GetModsDirectory(string gameDir) => Path.Combine(gameDir, "mods");
    public string SanitizeProfileFolderName(string profileName) => SanitizeFolderName(profileName ?? "Default");
    public string GetResourcePacksDirectory(string gameDir) => Path.Combine(gameDir, "resourcepacks");
    public string GetShaderPacksDirectory(string gameDir) => Path.Combine(gameDir, "shaderpacks");
    public string GetSavesDirectory(string gameDir) => Path.Combine(gameDir, "saves");

    #endregion

    #region Obtención de versiones disponibles

    public async Task<List<GameVersion>> FetchAvailableVersionsAsync()
    {
        try
        {
            _logger.LogInformation("Fetching Minecraft version manifest...");
            var json = await _httpClient.GetStringAsync(VersionManifestUrl).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var versions = new List<GameVersion>();

            foreach (var v in doc.RootElement.GetProperty("versions").EnumerateArray())
            {
                versions.Add(new GameVersion
                {
                    VersionId = v.GetProperty("id").GetString() ?? "",
                    VersionType = v.GetProperty("type").GetString() ?? "release",
                    Url = v.GetProperty("url").GetString() ?? "",
                    ReleasedAt = v.TryGetProperty("releaseTime", out var t) ? t.GetDateTime() : DateTime.MinValue
                });
            }
            return versions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch version manifest");
            return new List<GameVersion>();
        }
    }

    #endregion

}
