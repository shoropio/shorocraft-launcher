using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Infrastructure.Downloading;

namespace ShoroCraftLauncher.Infrastructure.Services;

public class ShaderPackService : IShaderPackService
{
    private readonly IShaderPackRepository _repository;
    private readonly IProfileRepository _profileRepository;
    private readonly IModRepository _modRepository;
    private readonly IMinecraftService _minecraftService;
    private readonly ILogger<ShaderPackService> _logger;
    private readonly ILogService _logService;
    private readonly HttpClient _httpClient;
    private readonly IResumableDownloadService _resumableDownloadService;

    public ShaderPackService(
        IShaderPackRepository repository,
        IProfileRepository profileRepository,
        IModRepository modRepository,
        IMinecraftService minecraftService,
        ILogger<ShaderPackService> logger,
        ILogService logService,
        HttpClient httpClient,
        IResumableDownloadService? resumableDownloadService = null)
    {
        _repository = repository;
        _profileRepository = profileRepository;
        _modRepository = modRepository;
        _minecraftService = minecraftService;
        _logger = logger;
        _logService = logService;
        _httpClient = httpClient;
        _resumableDownloadService = resumableDownloadService ?? new ResumableDownloadService(httpClient);
    }

    public async Task<List<ShaderPack>> GetPacksAsync(int profileId) =>
        await _repository.GetByProfileIdAsync(profileId);

    public async Task<ShaderPack> AddPackAsync(int profileId, string sourceFilePath)
    {
        _logger.LogInformation("Adding shader pack from {Source}", sourceFilePath);
        _logService.Info("ShaderPackService", "AddPack", $"Agregando shader {Path.GetFileName(sourceFilePath)}...");

        var ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        if (ext != ".zip")
            throw new Exception("Solo se permiten archivos .zip como shader packs.");

        var packsDir = await GetPacksFolderAsync(profileId);
        Directory.CreateDirectory(packsDir);

        var fileName = Path.GetFileName(sourceFilePath);
        var destPath = Path.Combine(packsDir, fileName);

        if (File.Exists(destPath))
            throw new Exception($"El shader pack '{fileName}' ya existe.");

        _logService.Info("ShaderPackService", "AddPack", $"Copiando {fileName} a shaderpacks...");
        File.Copy(sourceFilePath, destPath);

        var pack = new ShaderPack
        {
            ProfileId = profileId,
            Name = Path.GetFileNameWithoutExtension(fileName),
            FileName = fileName,
            FilePath = destPath,
            FileSizeBytes = new FileInfo(destPath).Length,
            Status = PackStatus.Active
        };

        await _repository.CreateAsync(pack);
        _logService.Info("ShaderPackService", "AddPack", $"Shader '{pack.Name}' agregado.");
        return pack;
    }

    public async Task TogglePackAsync(int packId)
    {
        var pack = await _repository.GetByIdAsync(packId)
            ?? throw new Exception($"Shader pack {packId} not found");
        pack.Status = pack.Status == PackStatus.Active ? PackStatus.Inactive : PackStatus.Active;
        await _repository.UpdateAsync(pack);
        _logService.Info("ShaderPackService", "TogglePack", $"Shader '{pack.Name}' {(pack.Status == PackStatus.Active ? "activado" : "desactivado")}.");
    }

    public async Task RemovePackAsync(int packId)
    {
        var pack = await _repository.GetByIdAsync(packId)
            ?? throw new Exception($"Shader pack {packId} not found");

        _logService.Info("ShaderPackService", "RemovePack", $"Eliminando shader '{pack.Name}'...");
        try { if (File.Exists(pack.FilePath)) File.Delete(pack.FilePath); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete shader file"); }

        await _repository.DeleteAsync(packId);
        _logService.Info("ShaderPackService", "RemovePack", $"Shader '{pack.Name}' eliminado.");
    }

    public async Task<string> GetPacksFolderAsync(int profileId)
    {
        var profile = await _profileRepository.GetByIdAsync(profileId)
            ?? throw new Exception($"Profile {profileId} not found");
        var gameDir = string.IsNullOrEmpty(profile.GameDirectory)
            ? _minecraftService.GetDefaultGameDirectory(profile.Name)
            : profile.GameDirectory;
        return _minecraftService.GetShaderPacksDirectory(gameDir);
    }

    public async Task<bool> HasShaderSupportAsync(int profileId)
    {
        var profile = await _profileRepository.GetByIdAsync(profileId)
            ?? throw new Exception($"Profile {profileId} not found");

        if (profile.Type is ProfileType.OptiFine or ProfileType.Iris)
            return true;

        var mods = await _modRepository.GetByProfileIdAsync(profileId);
        if (mods.Any(IsActiveShaderMod))
            return true;

        var gameDir = string.IsNullOrEmpty(profile.GameDirectory)
            ? _minecraftService.GetDefaultGameDirectory(profile.Name)
            : profile.GameDirectory;
        var modsDir = _minecraftService.GetModsDirectory(gameDir);

        if (!Directory.Exists(modsDir))
            return false;

        return Directory.GetFiles(modsDir, "*.jar")
            .Select(Path.GetFileNameWithoutExtension)
            .Any(IsShaderModName);
    }

    private static bool IsActiveShaderMod(Mod mod)
    {
        if (mod.Status != ModStatus.Active)
            return false;

        return IsShaderModName(mod.Name)
            || IsShaderModName(mod.FileName)
            || IsShaderModName(mod.ModVersion);
    }

    private static bool IsShaderModName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Contains("iris", StringComparison.OrdinalIgnoreCase)
            || value.Contains("oculus", StringComparison.OrdinalIgnoreCase)
            || value.Contains("optifine", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<List<ShaderPackSearchResult>> SearchShadersAsync(string query, string minecraftVersion)
    {
        _logger.LogInformation("Searching Modrinth shaders: {Query} for MC {Version}", query, minecraftVersion);
        _logService.Info("ShaderPackService", "SearchShaders",
            $"Buscando shaders en Modrinth{(string.IsNullOrWhiteSpace(query) ? " (populares)" : $" para '{query}'")}...");

        var facets = new List<string> { "[\"project_type:shader\"]" };
        if (!string.IsNullOrWhiteSpace(query))
            facets.Add($"[\"versions:{minecraftVersion}\"]");

        var url = "https://api.modrinth.com/v2/search"
            + $"?query={Uri.EscapeDataString(query.Trim())}"
            + $"&facets={Uri.EscapeDataString("[" + string.Join(",", facets) + "]")}"
            + "&limit=40&sort=downloads";

        EnsureUserAgent();
        using var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        var results = new List<ShaderPackSearchResult>();
        foreach (var item in doc.RootElement.GetProperty("hits").EnumerateArray())
        {
            results.Add(new ShaderPackSearchResult
            {
                ProjectId = item.GetProperty("project_id").GetString() ?? string.Empty,
                Name = item.GetProperty("title").GetString() ?? "Unknown",
                Description = item.TryGetProperty("description", out var d) ? d.GetString() : null,
                IconPath = item.TryGetProperty("icon_url", out var icon) ? icon.GetString() : null,
                ModVersion = (item.TryGetProperty("latest_version", out var v) ? v.GetString() : "latest") ?? "latest"
            });
        }

        _logService.Info("ShaderPackService", "SearchShaders", $"Encontrados {results.Count} shader packs.");
        return results;
    }

    public async Task<ShaderPack> InstallFromSearchAsync(int profileId, ShaderPackSearchResult searchResult)
    {
        var profile = await _profileRepository.GetByIdAsync(profileId)
            ?? throw new Exception($"Profile {profileId} not found");

        _logService.Info("ShaderPackService", "InstallFromSearch",
            $"Instalando shader '{searchResult.Name}' desde Modrinth...");

        var packsDir = await GetPacksFolderAsync(profileId);
        Directory.CreateDirectory(packsDir);

        var (downloadUrl, fileName, version, size) =
            await ResolveShaderVersionAsync(searchResult.ProjectId, profile.MinecraftVersion);

        var destPath = Path.Combine(packsDir, fileName);
        var tempPath = destPath + ".tmp";

        // Localiza una instalación previa del mismo pack, pero NO la borra todavía:
        // si la descarga falla, el pack original queda intacto.
        var existingPacks = await _repository.GetByProfileIdAsync(profileId);
        var existing = existingPacks.FirstOrDefault(p =>
            string.Equals(p.FileName, fileName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.Name, searchResult.Name, StringComparison.OrdinalIgnoreCase));

        _logger.LogInformation("Downloading shader from {Url}", downloadUrl);
        _logService.Info("ShaderPackService", "DownloadShader", $"Descargando {fileName}...");
        try
        {
            await _resumableDownloadService.DownloadAsync(downloadUrl, tempPath);
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }
        var downloadedBytes = new FileInfo(tempPath).Length;
        _logService.Info("ShaderPackService", "DownloadShader", $"Descarga completada ({downloadedBytes} bytes).");

        // Descarga OK: ahora sí reemplaza la instalación previa.
        if (existing != null)
        {
            try
            {
                if (File.Exists(existing.FilePath)) File.Delete(existing.FilePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete previous file {Path}", existing.FilePath);
            }
            await _repository.DeleteAsync(existing.Id);
            _logService.Info("ShaderPackService", "InstallFromSearch",
                $"Reemplazando '{existing.FileName}' por '{fileName}'.");
        }
        else
        {
            if (File.Exists(destPath)) File.Delete(destPath);
        }

        File.Move(tempPath, destPath);

        var pack = new ShaderPack
        {
            ProfileId = profileId,
            Name = searchResult.Name,
            FileName = fileName,
            FilePath = destPath,
            FileSizeBytes = size > 0 ? size : downloadedBytes,
            Status = PackStatus.Active
        };

        await _repository.CreateAsync(pack);
        _logger.LogInformation("Shader {Name} installed ({Version})", pack.Name, version);
        _logService.Info("ShaderPackService", "InstallFromSearch", $"Shader '{searchResult.Name}' instalado correctamente.");
        return pack;
    }

    private async Task<(string url, string fileName, string version, long size)> ResolveShaderVersionAsync(string projectId, string mcVersion)
    {
        EnsureUserAgent();
        var response = await _httpClient.GetAsync($"https://api.modrinth.com/v2/project/{projectId}/version");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        var candidates = new List<System.Text.Json.JsonElement>();
        foreach (var version in doc.RootElement.EnumerateArray())
        {
            bool matchesMc = false;
            foreach (var gv in version.GetProperty("game_versions").EnumerateArray())
            {
                if (gv.GetString() == mcVersion) { matchesMc = true; break; }
            }
            // El perfil por defecto se crea con "latest": aceptar cualquier versión del juego.
            if (!matchesMc && mcVersion.Equals("latest", StringComparison.OrdinalIgnoreCase))
                matchesMc = true;
            if (!matchesMc) continue;

            if (version.GetProperty("files").GetArrayLength() == 0) continue;
            if (!HasZipFile(version)) continue;

            candidates.Add(version);
        }

        System.Text.Json.JsonElement selected = default;
        var selectedDate = DateTimeOffset.MinValue;
        foreach (var candidate in candidates)
        {
            var type = candidate.TryGetProperty("version_type", out var vt) ? vt.GetString() : null;
            if (!string.Equals(type, "release", StringComparison.OrdinalIgnoreCase)) continue;

            if (TryGetDate(candidate, out var date) && date >= selectedDate)
            {
                selected = candidate;
                selectedDate = date;
            }
        }
        if (selected.ValueKind == System.Text.Json.JsonValueKind.Undefined && candidates.Count > 0)
        {
            foreach (var candidate in candidates)
            {
                if (TryGetDate(candidate, out var date) && date >= selectedDate)
                {
                    selected = candidate;
                    selectedDate = date;
                }
            }
        }

        if (selected.ValueKind == System.Text.Json.JsonValueKind.Undefined)
            throw new Exception($"No se encontró una versión del shader pack compatible con Minecraft {mcVersion}.");

        var versionElement = selected;
        var files = versionElement.GetProperty("files");
        var file = default(System.Text.Json.JsonElement);
        foreach (var f in files.EnumerateArray())
        {
            var fName = f.GetProperty("filename").GetString() ?? "";
            if (fName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) { file = f; break; }
        }
        if (file.ValueKind == System.Text.Json.JsonValueKind.Undefined)
            file = files[0];

        return (
            file.GetProperty("url").GetString() ?? "",
            file.GetProperty("filename").GetString() ?? $"{projectId}.zip",
            versionElement.GetProperty("version_number").GetString() ?? "unknown",
            file.TryGetProperty("size", out var s) ? s.GetInt64() : 0L);
    }

    private static bool HasZipFile(System.Text.Json.JsonElement version)
    {
        foreach (var f in version.GetProperty("files").EnumerateArray())
        {
            var fName = f.GetProperty("filename").GetString() ?? "";
            if (fName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool TryGetDate(System.Text.Json.JsonElement version, out DateTimeOffset date)
    {
        date = DateTimeOffset.MinValue;
        return version.TryGetProperty("date_published", out var dp)
            && DateTimeOffset.TryParse(dp.GetString(), out date);
    }

    private static readonly string[] RecommendedShaderProjectIds =
    {
        "HVnmMxH1", // Complementary Reimagined
        "Q1vvjJYV", // BSL Shaders
        "lLqFfGNs", // Photon Shaders
        "EpQFjzrQ", // Solas Shader
        "ZvMtQlho", // Bliss Shaders
        "izsIPI7a", // MakeUp - Ultra Fast
        "LMIZZNxZ", // Super Duper Vanilla
        "kmwfVOoi", // Rethinking Voxels
    };

    public async Task<List<ShaderPackSearchResult>> GetRecommendedShadersAsync()
    {
        _logService.Info("ShaderPackService", "GetRecommended", "Cargando shader packs recomendados...");
        var results = new List<ShaderPackSearchResult>();
        foreach (var projectId in RecommendedShaderProjectIds)
        {
            var project = await GetProjectAsync(projectId);
            if (project != null) results.Add(project);
        }
        _logService.Info("ShaderPackService", "GetRecommended", $"Cargados {results.Count} shader packs recomendados.");
        return results;
    }

    private async Task<ShaderPackSearchResult?> GetProjectAsync(string projectId)
    {
        try
        {
            EnsureUserAgent();
            var response = await _httpClient.GetAsync($"https://api.modrinth.com/v2/project/{projectId}");
            response.EnsureSuccessStatusCode();

            using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            return new ShaderPackSearchResult
            {
                ProjectId = projectId,
                Name = root.TryGetProperty("title", out var t) ? t.GetString() ?? projectId : projectId,
                Description = root.TryGetProperty("description", out var d) ? d.GetString() : null,
                IconPath = root.TryGetProperty("icon_url", out var i) ? i.GetString() : null,
                ModVersion = (root.TryGetProperty("latest_version", out var v) ? v.GetString() : "latest") ?? "latest"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load Modrinth project {ProjectId}", projectId);
            return null;
        }
    }

    private void EnsureUserAgent()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ShoroCraftLauncher/1.0.0");
    }
}
