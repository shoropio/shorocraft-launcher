using System.IO.Compression;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Infrastructure.Services;

public class ModService : IModService
{
    private readonly IModRepository _modRepository;
    private readonly IProfileRepository _profileRepository;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IMinecraftService _minecraftService;
    private readonly ILogger<ModService> _logger;
    private readonly ILogService _logService;
    private readonly HttpClient _httpClient;

    public ModService(
        IModRepository modRepository,
        IProfileRepository profileRepository,
        ISettingsRepository settingsRepository,
        IMinecraftService minecraftService,
        ILogger<ModService> logger,
        ILogService logService,
        HttpClient httpClient)
    {
        _modRepository = modRepository;
        _profileRepository = profileRepository;
        _settingsRepository = settingsRepository;
        _minecraftService = minecraftService;
        _logger = logger;
        _logService = logService;
        _httpClient = httpClient;
    }

    public async Task<List<Mod>> SearchModsAsync(string provider, string query, string minecraftVersion, string loaderType)
    {
        return provider.Equals("CurseForge", StringComparison.OrdinalIgnoreCase)
            ? await SearchCurseForgeAsync(query, minecraftVersion, loaderType)
            : await SearchModrinthAsync(query, minecraftVersion, loaderType);
    }

    public async Task<List<Mod>> SearchModrinthAsync(string query, string minecraftVersion, string loaderType)
    {
        _logger.LogInformation("Searching Modrinth: {Query} for MC {Version} on {Loader}", query, minecraftVersion, loaderType);
        _logService.Info("ModService", "SearchModrinth", $"Buscando '{query}' en Modrinth para MC {minecraftVersion} ({loaderType})...");
        
        try
        {
            var url = $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(query)}&facets=[[\"versions:{minecraftVersion}\"],[\"categories:{loaderType.ToLower()}\"]]";
            _httpClient.DefaultRequestHeaders.UserAgent.Clear();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ShoroCraftLauncher/1.0.0");
            
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            
            var json = await response.Content.ReadAsStringAsync();
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
            _logService.Info("ModService", "SearchModrinth", $"Encontrados {results.Count} resultados.");
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Modrinth search failed");
            _logService.Error("ModService", "SearchModrinth", $"Error en búsqueda: {ex.Message}", ex);
            return new List<Mod>();
        }
    }

    private async Task<List<Mod>> SearchCurseForgeAsync(string query, string minecraftVersion, string loaderType)
    {
        var apiKey = await _settingsRepository.GetAsync("curseforge_api_key");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Configura una API key de CurseForge en Configuración.");

        var loaderTypeId = loaderType.ToLowerInvariant() switch
        {
            "forge" => 1,
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

        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
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

    public async Task<Mod> InstallFromSearchAsync(int profileId, Mod searchResult, string provider)
    {
        var profile = await _profileRepository.GetByIdAsync(profileId)
            ?? throw new Exception($"Profile {profileId} not found");

        _logService.Info("ModService", "InstallFromSearch", $"Instalando '{searchResult.Name}' desde {provider}...");

        var modsDir = await GetModsFolderAsync(profileId);
        Directory.CreateDirectory(modsDir);

        string downloadUrl;
        string fileName;
        string modVersion;
        long fileSize;

        if (provider.Equals("CurseForge", StringComparison.OrdinalIgnoreCase))
            (downloadUrl, fileName, modVersion, fileSize) = await ResolveCurseForgeDownloadAsync(searchResult, profile);
        else
            (downloadUrl, fileName, modVersion, fileSize) = await ResolveModrinthDownloadAsync(searchResult, profile);

        var destPath = Path.Combine(modsDir, fileName);

        if (File.Exists(destPath))
            throw new Exception($"El mod '{fileName}' ya existe en este perfil.");

        _logger.LogInformation("Downloading mod from {Url}", downloadUrl);
        _logService.Info("ModService", "DownloadMod", $"Descargando {fileName} ({FormatFileSize(fileSize)})...");
        var bytes = await _httpClient.GetByteArrayAsync(downloadUrl);
        await File.WriteAllBytesAsync(destPath, bytes);
        _logService.Info("ModService", "DownloadMod", $"Descarga completada ({FormatFileSize(bytes.Length)}).");

        var mod = new Mod
        {
            ProfileId = profileId,
            Name = searchResult.Name,
            FileName = fileName,
            FilePath = destPath,
            FileSizeBytes = fileSize > 0 ? fileSize : bytes.Length,
            MinecraftVersion = profile.MinecraftVersion,
            ModVersion = modVersion,
            IconPath = searchResult.IconPath,
            Description = searchResult.Description,
            Status = ModStatus.Active
        };

        await _modRepository.CreateAsync(mod);
        _logger.LogInformation("Mod {Name} installed from {Provider}", mod.Name, provider);
        _logService.Info("ModService", "InstallFromSearch", $"'{searchResult.Name}' instalado correctamente.");
        return mod;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }

    private async Task<(string url, string fileName, string version, long size)> ResolveModrinthDownloadAsync(Mod searchResult, Profile profile)
    {
        var projectId = searchResult.FileName;
        var url = $"https://api.modrinth.com/v2/project/{projectId}/version";

        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ShoroCraftLauncher/1.0.0");

        var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        var loader = profile.Type.ToString().ToLowerInvariant();
        var mcVersion = profile.MinecraftVersion;

        foreach (var version in doc.RootElement.EnumerateArray())
        {
            var gameVersions = version.GetProperty("game_versions");
            bool matchesMc = false;
            foreach (var gv in gameVersions.EnumerateArray())
            {
                if (gv.GetString() == mcVersion) { matchesMc = true; break; }
            }
            if (!matchesMc) continue;

            var loaders = version.GetProperty("loaders");
            bool matchesLoader = false;
            foreach (var l in loaders.EnumerateArray())
            {
                if (l.GetString() == loader) { matchesLoader = true; break; }
            }
            if (!matchesLoader) continue;

            var files = version.GetProperty("files");
            if (files.GetArrayLength() > 0)
            {
                var file = files[0];
                var fileUrl = file.GetProperty("url").GetString() ?? "";
                var fName = file.GetProperty("filename").GetString() ?? $"{projectId}.jar";
                var fSize = file.TryGetProperty("size", out var s) ? s.GetInt64() : 0L;
                var vStr = version.GetProperty("version_number").GetString() ?? "unknown";
                return (fileUrl, fName, vStr, fSize);
            }
        }

        throw new Exception($"No se encontró una versión de '{searchResult.Name}' compatible con Minecraft {mcVersion} ({loader}).");
    }

    private async Task<(string url, string fileName, string version, long size)> ResolveCurseForgeDownloadAsync(Mod searchResult, Profile profile)
    {
        var apiKey = await _settingsRepository.GetAsync("curseforge_api_key");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Configura una API key de CurseForge en Configuración.");

        var modId = searchResult.FileName;
        var url = $"https://api.curseforge.com/v1/mods/{modId}/files?pageSize=1&sortField=1&sortOrder=desc";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.UserAgent.ParseAdd("ShoroCraftLauncher/1.0.0");

        using var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        var loaderTypeId = profile.Type.ToString().ToLowerInvariant() switch
        {
            "forge" => 1,
            "fabric" => 4,
            "quilt" => 5,
            _ => 0
        };

        foreach (var file in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            var gameVersions = file.GetProperty("gameVersions");
            bool matchesMc = false;
            foreach (var gv in gameVersions.EnumerateArray())
            {
                if (gv.GetString() == profile.MinecraftVersion) { matchesMc = true; break; }
            }
            if (!matchesMc) continue;

            if (loaderTypeId != 0)
            {
                bool matchesLoader = false;
                foreach (var gv in gameVersions.EnumerateArray())
                {
                    if (gv.ValueKind == System.Text.Json.JsonValueKind.Number && gv.GetInt32() == loaderTypeId)
                    { matchesLoader = true; break; }
                }
                if (!matchesLoader) continue;
            }

            var downloadUrl = file.GetProperty("downloadUrl").GetString();
            if (string.IsNullOrEmpty(downloadUrl)) continue;

            var fName = file.GetProperty("fileName").GetString() ?? $"{modId}.jar";
            var fSize = file.TryGetProperty("fileSize", out var s) ? s.GetInt64() : 0L;
            var vStr = file.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "unknown" : "unknown";

            return (downloadUrl, fName, vStr, fSize);
        }

        throw new Exception($"No se encontró una versión de '{searchResult.Name}' compatible con Minecraft {profile.MinecraftVersion}.");
    }

    public async Task<List<Mod>> GetModsAsync(int profileId) =>
        await _modRepository.GetByProfileIdAsync(profileId);

    public async Task<Mod> AddModAsync(int profileId, string sourceFilePath)
    {
        _logger.LogInformation("Adding mod from {Source} to profile {ProfileId}", sourceFilePath, profileId);
        _logService.Info("ModService", "AddMod", $"Agregando mod desde {Path.GetFileName(sourceFilePath)}...");

        var profile = await _profileRepository.GetByIdAsync(profileId)
            ?? throw new Exception($"Profile {profileId} not found");

        var extension = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        if (extension != ".jar")
            throw new Exception("Solo se permiten archivos .jar como mods.");

        var modsDir = await GetModsFolderAsync(profileId);
        Directory.CreateDirectory(modsDir);

        var fileName = Path.GetFileName(sourceFilePath);
        var destPath = Path.Combine(modsDir, fileName);

        if (File.Exists(destPath))
            throw new Exception($"El mod '{fileName}' ya existe en este perfil.");

        File.Copy(sourceFilePath, destPath, false);

        var modInfo = await ExtractModInfoAsync(destPath);
        var mod = new Mod
        {
            ProfileId = profileId,
            Name = modInfo.Name ?? Path.GetFileNameWithoutExtension(fileName),
            FileName = fileName,
            FilePath = destPath,
            FileSizeBytes = new FileInfo(destPath).Length,
            MinecraftVersion = modInfo.MinecraftVersion ?? profile.MinecraftVersion,
            ModVersion = modInfo.ModVersion ?? "unknown",
            Status = ModStatus.Active
        };

        await _modRepository.CreateAsync(mod);
        _logger.LogInformation("Mod {Name} added successfully", mod.Name);
        _logService.Info("ModService", "AddMod", $"Mod '{mod.Name}' agregado.");
        return mod;
    }

    public async Task ToggleModAsync(int modId)
    {
        var mod = await _modRepository.GetByIdAsync(modId)
            ?? throw new Exception($"Mod {modId} not found");

        mod.Status = mod.Status == ModStatus.Active ? ModStatus.Inactive : ModStatus.Active;
        await _modRepository.UpdateAsync(mod);
        _logger.LogInformation("Mod {Name} toggled to {Status}", mod.Name, mod.Status);
        _logService.Info("ModService", "ToggleMod", $"Mod '{mod.Name}' {(mod.Status == ModStatus.Active ? "activado" : "desactivado")}.");
    }

    public async Task RemoveModAsync(int modId)
    {
        var mod = await _modRepository.GetByIdAsync(modId)
            ?? throw new Exception($"Mod {modId} not found");

        try
        {
            if (File.Exists(mod.FilePath))
                File.Delete(mod.FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete mod file {Path}", mod.FilePath);
        }

        await _modRepository.DeleteAsync(modId);
        _logger.LogInformation("Mod {Name} removed", mod.Name);
        _logService.Info("ModService", "RemoveMod", $"Mod '{mod.Name}' eliminado.");
    }

    public async Task<string> GetModsFolderAsync(int profileId)
    {
        var profile = await _profileRepository.GetByIdAsync(profileId)
            ?? throw new Exception($"Profile {profileId} not found");
        var gameDir = string.IsNullOrEmpty(profile.GameDirectory)
            ? _minecraftService.GetDefaultGameDirectory(profile.Name)
            : profile.GameDirectory;
        return _minecraftService.GetModsDirectory(gameDir);
    }

    public async Task<(string? Name, string? MinecraftVersion, string? ModVersion)> ExtractModInfoAsync(string jarPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jarPath);
            var mcmodEntry = archive.GetEntry("mcmod.info");
            if (mcmodEntry != null)
            {
                using var reader = new StreamReader(mcmodEntry.Open());
                var content = await reader.ReadToEndAsync();
                return ParseMcmodInfo(content);
            }

            var fabricEntry = archive.Entries.FirstOrDefault(e =>
                e.FullName.StartsWith("fabric.mod.json", StringComparison.OrdinalIgnoreCase));
            if (fabricEntry != null)
            {
                using var reader = new StreamReader(fabricEntry.Open());
                var content = await reader.ReadToEndAsync();
                return ParseFabricModJson(content);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract mod info from {Jar}", jarPath);
        }

        return (null, null, null);
    }

    private (string? Name, string? MinecraftVersion, string? ModVersion) ParseMcmodInfo(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == System.Text.Json.JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var first = root[0];
                return (
                    first.TryGetProperty("name", out var n) ? n.GetString() : null,
                    first.TryGetProperty("mcversion", out var mv) ? mv.GetString() : null,
                    first.TryGetProperty("version", out var v) ? v.GetString() : null
                );
            }
        }
        catch { }
        return (null, null, null);
    }

    private (string? Name, string? MinecraftVersion, string? ModVersion) ParseFabricModJson(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;

            string? mcVersion = null;
            if (root.TryGetProperty("depends", out var depends))
            {
                if (depends.TryGetProperty("minecraft", out var mc))
                    mcVersion = mc.GetString();
            }

            string? modVersion = null;
            if (root.TryGetProperty("version", out var v))
                modVersion = v.GetString();

            return (name, mcVersion, modVersion);
        }
        catch { }
        return (null, null, null);
    }
}
