using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Infrastructure.Downloading;

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
    private readonly IResumableDownloadService _resumableDownloadService;

    public ModService(
        IModRepository modRepository,
        IProfileRepository profileRepository,
        ISettingsRepository settingsRepository,
        IMinecraftService minecraftService,
        ILogger<ModService> logger,
        ILogService logService,
        HttpClient httpClient,
        IResumableDownloadService? resumableDownloadService = null)
    {
        _modRepository = modRepository;
        _profileRepository = profileRepository;
        _settingsRepository = settingsRepository;
        _minecraftService = minecraftService;
        _logger = logger;
        _logService = logService;
        _httpClient = httpClient;
        _resumableDownloadService = resumableDownloadService ?? new ResumableDownloadService(httpClient);
    }

    public async Task<List<Mod>> SearchModsAsync(string provider, string query, string minecraftVersion, string loaderType)
    {
        return provider.Equals("CurseForge", StringComparison.OrdinalIgnoreCase)
            ? await SearchCurseForgeAsync(query, minecraftVersion, loaderType)
            : await SearchModrinthAsync(query, minecraftVersion, loaderType);
    }

    public async Task<List<Mod>> SearchModrinthAsync(string query, string minecraftVersion, string loaderType)
    {
        var modrinthVersion = ToModrinthVersion(minecraftVersion);
        _logger.LogInformation("Searching Modrinth: {Query} for MC {Version} (Modrinth: {ModrinthVersion}) on {Loader}", query, minecraftVersion, modrinthVersion, loaderType);
        _logService.Info("ModService", "SearchModrinth", $"Buscando '{query}' en Modrinth para MC {minecraftVersion} ({loaderType})...");
        
        try
        {
            var url = $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(query)}&facets=[[\"versions:{modrinthVersion}\"],[\"categories:{loaderType.ToLower()}\"]]";
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
        string projectSlug = string.Empty;
        IReadOnlyList<ModrinthDependency>? dependencies = null;

        if (provider.Equals("CurseForge", StringComparison.OrdinalIgnoreCase))
            (downloadUrl, fileName, modVersion, fileSize) = await ResolveCurseForgeDownloadAsync(searchResult, profile);
        else
        {
            var resolved = await ResolveModrinthDownloadAsync(searchResult, profile);
            downloadUrl = resolved.Url;
            fileName = resolved.FileName;
            modVersion = resolved.Version;
            fileSize = resolved.Size;
            dependencies = resolved.Dependencies;
            projectSlug = resolved.ProjectSlug;
        }

        var destPath = Path.Combine(modsDir, fileName);
        var tempPath = destPath + ".tmp";

        // Localiza una instalación previa del mismo mod, pero NO la borra todavía:
        // si la descarga falla, el mod original queda intacto.
        var existingMods = await _modRepository.GetByProfileIdAsync(profileId);
        var existing = string.IsNullOrWhiteSpace(projectSlug)
            ? existingMods.FirstOrDefault(m => string.Equals(m.FileName, fileName, StringComparison.OrdinalIgnoreCase))
            : existingMods.FirstOrDefault(m => StartsWithModSlug(m.FileName, projectSlug));

        _logger.LogInformation("Downloading mod from {Url}", downloadUrl);
        _logService.Info("ModService", "DownloadMod", $"Descargando {fileName} ({FormatFileSize(fileSize)})...");
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
        _logService.Info("ModService", "DownloadMod", $"Descarga completada ({FormatFileSize(downloadedBytes)}).");

        // Descarga OK: ahora sí reemplaza la instalación previa.
        if (existing != null)
        {
            try
            {
                if (File.Exists(existing.FilePath)) File.Delete(existing.FilePath);
                var disabledPath = existing.FilePath + ".disabled";
                if (File.Exists(disabledPath)) File.Delete(disabledPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete previous file {Path}", existing.FilePath);
            }
            await _modRepository.DeleteAsync(existing.Id);
            _logService.Info("ModService", "InstallFromSearch",
                $"Reemplazando '{existing.FileName}' por '{fileName}'.");
        }
        else
        {
            if (File.Exists(destPath + ".disabled")) File.Delete(destPath + ".disabled");
            if (File.Exists(destPath)) File.Delete(destPath);
        }

        File.Move(tempPath, destPath);

        var mod = new Mod
        {
            ProfileId = profileId,
            Name = searchResult.Name,
            FileName = fileName,
            FilePath = destPath,
            FileSizeBytes = fileSize > 0 ? fileSize : downloadedBytes,
            MinecraftVersion = profile.MinecraftVersion,
            ModVersion = modVersion,
            IconPath = searchResult.IconPath,
            Description = searchResult.Description,
            Status = ModStatus.Active,
            SourceProvider = provider,
            RemoteProjectId = searchResult.FileName,
            RemoteSlug = projectSlug
        };

        await _modRepository.CreateAsync(mod);
        _logger.LogInformation("Mod {Name} installed from {Provider}", mod.Name, provider);
        _logService.Info("ModService", "InstallFromSearch", $"'{searchResult.Name}' instalado correctamente.");

        if (dependencies is { Count: > 0 })
            await InstallRequiredDependenciesAsync(profileId, modsDir, profile, dependencies);

        return mod;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }

    private async Task<ModrinthVersionInfo> ResolveModrinthDownloadAsync(Mod searchResult, Profile profile)
    {
        var projectId = searchResult.FileName;
        var projectSlug = await GetProjectSlugAsync(projectId);
        EnsureUserAgent();

        var response = await _httpClient.GetAsync($"https://api.modrinth.com/v2/project/{projectId}/version");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        var loader = profile.Type.ToString().ToLowerInvariant();
        var originalMcVersion = profile.MinecraftVersion;
        var searchMcVersion = ToModrinthVersion(originalMcVersion);

        var resolved = FindBestVersion(doc, originalMcVersion, searchMcVersion, loader, projectId, preferRelease: true, projectSlug);
        if (resolved == null)
        {
            _logService.Warning("ModService", "ResolveVersion",
                $"No hay versión estable de '{searchResult.Name}' para Minecraft {originalMcVersion} ({loader}); se usará la última versión disponible.");
            resolved = FindBestVersion(doc, originalMcVersion, searchMcVersion, loader, projectId, preferRelease: false, projectSlug);
        }

        if (resolved == null)
            throw new Exception($"No se encontró una versión de '{searchResult.Name}' compatible con Minecraft {originalMcVersion} ({loader}).");

        return resolved with { ProjectSlug = projectSlug };
    }

    private static ModrinthVersionInfo? FindBestVersion(
        System.Text.Json.JsonDocument doc,
        string originalMcVersion,
        string searchMcVersion,
        string loader,
        string projectId,
        bool preferRelease,
        string? projectSlug = null)
    {
        ModrinthVersionInfo? best = null;
        var bestDate = DateTimeOffset.MinValue;

        foreach (var version in doc.RootElement.EnumerateArray())
        {
            if (preferRelease)
            {
                var versionType = version.TryGetProperty("version_type", out var vt) ? vt.GetString() : null;
                if (!string.Equals(versionType, "release", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            bool matchesMc = false;
            foreach (var gv in version.GetProperty("game_versions").EnumerateArray())
            {
                var gvStr = gv.GetString();
                if (gvStr == originalMcVersion || gvStr == searchMcVersion) { matchesMc = true; break; }
            }
            if (!matchesMc && originalMcVersion.Equals("latest", StringComparison.OrdinalIgnoreCase))
                matchesMc = true;
            if (!matchesMc) continue;

            bool matchesLoader = false;
            foreach (var l in version.GetProperty("loaders").EnumerateArray())
            {
                if (l.GetString() == loader) { matchesLoader = true; break; }
            }
            if (!matchesLoader) continue;

            var files = version.GetProperty("files");
            if (files.GetArrayLength() == 0) continue;

            var file = PickBestFile(files, projectSlug);
            if (file is null) continue;
            var pickedFile = file.Value;

            var date = DateTimeOffset.MinValue;
            if (version.TryGetProperty("date_published", out var dp)
                && DateTimeOffset.TryParse(dp.GetString(), out var parsed))
                date = parsed;

            if (date < bestDate) continue;

            var fileUrl = pickedFile.GetProperty("url").GetString() ?? "";
            var fName = pickedFile.GetProperty("filename").GetString() ?? $"{projectId}.jar";
            var fSize = pickedFile.TryGetProperty("size", out var s) ? s.GetInt64() : 0L;
            var vStr = version.GetProperty("version_number").GetString() ?? "unknown";
            best = new ModrinthVersionInfo(fileUrl, fName, vStr, fSize, string.Empty, ParseDependencies(version));
            bestDate = date;
        }

        return best;
    }

    private static System.Text.Json.JsonElement? PickBestFile(System.Text.Json.JsonElement files, string? projectSlug)
    {
        System.Text.Json.JsonElement? fallback = null;
        foreach (var file in files.EnumerateArray())
        {
            fallback ??= file;
            if (!string.IsNullOrWhiteSpace(projectSlug))
            {
                var fName = file.TryGetProperty("filename", out var fn) ? fn.GetString() ?? string.Empty : string.Empty;
                if (StartsWithModSlug(fName, projectSlug)
                    || fName.Contains(projectSlug + "-", StringComparison.OrdinalIgnoreCase)
                    || fName.Contains(projectSlug + "_", StringComparison.OrdinalIgnoreCase))
                    return file;
            }
        }
        return fallback;
    }

    private static ModrinthVersionInfo? FindVersionById(System.Text.Json.JsonDocument doc, string versionId)
    {
        foreach (var version in doc.RootElement.EnumerateArray())
        {
            if (!string.Equals(
                    version.TryGetProperty("id", out var id) ? id.GetString() : null,
                    versionId,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var files = version.GetProperty("files");
            if (files.GetArrayLength() == 0) continue;

            var file = files[0];
            return new ModrinthVersionInfo(
                file.GetProperty("url").GetString() ?? "",
                file.GetProperty("filename").GetString() ?? "",
                version.GetProperty("version_number").GetString() ?? "unknown",
                file.TryGetProperty("size", out var s) ? s.GetInt64() : 0L,
                string.Empty,
                ParseDependencies(version));
        }

        return null;
    }

    private static List<ModrinthDependency> ParseDependencies(System.Text.Json.JsonElement version)
    {
        var list = new List<ModrinthDependency>();
        if (!version.TryGetProperty("dependencies", out var dependencies)) return list;
        foreach (var d in dependencies.EnumerateArray())
        {
            var projectId = d.TryGetProperty("project_id", out var p) ? p.GetString() : null;
            if (string.IsNullOrWhiteSpace(projectId)) continue;
            var versionId = d.TryGetProperty("version_id", out var v) ? v.GetString() : null;
            var depType = d.TryGetProperty("dependency_type", out var t) ? t.GetString() : null;
            list.Add(new ModrinthDependency(projectId, versionId ?? "", depType ?? ""));
        }
        return list;
    }

    private async Task<string> GetProjectSlugAsync(string projectId)
        => (await GetProjectInfoAsync(projectId)).slug;

    private async Task<(string slug, string title, string icon)> GetProjectInfoAsync(string projectId)
    {
        try
        {
            EnsureUserAgent();
            var response = await _httpClient.GetAsync($"https://api.modrinth.com/v2/project/{projectId}");
            response.EnsureSuccessStatusCode();
            using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            var slug = root.TryGetProperty("slug", out var s) ? s.GetString() ?? projectId : projectId;
            var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? projectId : projectId;
            var icon = root.TryGetProperty("icon_url", out var i) ? i.GetString() ?? string.Empty : string.Empty;
            return (slug, title, icon);
        }
        catch
        {
            return (projectId, projectId, string.Empty);
        }
    }

    private void EnsureUserAgent()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ShoroCraftLauncher/1.0.0");
    }

    private async Task InstallRequiredDependenciesAsync(int profileId, string modsDir, Profile profile, IReadOnlyList<ModrinthDependency> dependencies)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await InstallDependenciesRecursiveAsync(profileId, modsDir, profile, dependencies, visited);
    }

    private async Task InstallDependenciesRecursiveAsync(int profileId, string modsDir, Profile profile, IReadOnlyList<ModrinthDependency> dependencies, HashSet<string> visited)
    {
        foreach (var dep in dependencies.Where(d => d.DependencyType.Equals("required", StringComparison.OrdinalIgnoreCase)))
            await InstallDependencyAsync(profileId, modsDir, profile, dep, visited);
    }

    private async Task InstallDependencyAsync(int profileId, string modsDir, Profile profile, ModrinthDependency dependency, HashSet<string> visited)
    {
        if (dependency.DependencyType.Equals("incompatible", StringComparison.OrdinalIgnoreCase))
            return;

        if (!visited.Add(dependency.ProjectId))
            return;

        var (slug, title, icon) = await GetProjectInfoAsync(dependency.ProjectId);
        var resolved = await ResolveDependencyVersionAsync(dependency.ProjectId, dependency.VersionId, profile);
        if (resolved == null)
        {
            _logService.Warning("ModService", "InstallDependency",
                $"No se encontró una versión compatible de la dependencia '{title}' para Minecraft {profile.MinecraftVersion} ({profile.Type}).");
            return;
        }

        var destPath = Path.Combine(modsDir, resolved.FileName);

        if (File.Exists(destPath) || File.Exists(destPath + ".disabled"))
        {
            _logService.Info("ModService", "InstallDependency", $"La dependencia '{title}' ya está instalada ({resolved.FileName}).");
            await InstallDependenciesRecursiveAsync(profileId, modsDir, profile, resolved.Dependencies, visited);
            return;
        }

        var existing = (await _modRepository.GetByProfileIdAsync(profileId))
            .FirstOrDefault(m => StartsWithModSlug(m.FileName, slug));
        if (existing != null)
        {
            try
            {
                if (File.Exists(existing.FilePath)) File.Delete(existing.FilePath);
                var disabledPath = existing.FilePath + ".disabled";
                if (File.Exists(disabledPath)) File.Delete(disabledPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete outdated dependency file {Path}", existing.FilePath);
            }
            await _modRepository.DeleteAsync(existing.Id);
            _logService.Info("ModService", "InstallDependency",
                $"Reemplazando '{existing.FileName}' por la versión requerida '{resolved.FileName}'.");
        }

        _logService.Info("ModService", "InstallDependency", $"Instalando dependencia '{title}' ({resolved.FileName})...");
        await _resumableDownloadService.DownloadAsync(resolved.Url, destPath);
        var dependencyBytes = new FileInfo(destPath).Length;

        var mod = new Mod
        {
            ProfileId = profileId,
            Name = title,
            FileName = resolved.FileName,
            FilePath = destPath,
            FileSizeBytes = resolved.Size > 0 ? resolved.Size : dependencyBytes,
            MinecraftVersion = profile.MinecraftVersion,
            ModVersion = resolved.Version,
            IconPath = icon,
            Description = "Instalado automáticamente como dependencia.",
            Status = ModStatus.Active
        };
        await _modRepository.CreateAsync(mod);
        _logService.Info("ModService", "InstallDependency", $"Dependencia '{title}' instalada correctamente.");

        await InstallDependenciesRecursiveAsync(profileId, modsDir, profile, resolved.Dependencies, visited);
    }

    private async Task<ModrinthVersionInfo?> ResolveDependencyVersionAsync(string projectId, string versionId, Profile profile)
    {
        EnsureUserAgent();
        var response = await _httpClient.GetAsync($"https://api.modrinth.com/v2/project/{projectId}/version");
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        var loader = profile.Type.ToString().ToLowerInvariant();
        var mcVersion = profile.MinecraftVersion;
        var searchMcVersion = ToModrinthVersion(mcVersion);

        if (!string.IsNullOrEmpty(versionId))
        {
            return FindVersionById(doc, versionId)
                ?? FindBestVersion(doc, mcVersion, searchMcVersion, loader, projectId, preferRelease: true, projectId)
                ?? FindBestVersion(doc, mcVersion, searchMcVersion, loader, projectId, preferRelease: false, projectId);
        }

        var resolved = FindBestVersion(doc, mcVersion, searchMcVersion, loader, projectId, preferRelease: true, projectId);
        if (resolved == null)
        {
            _logService.Warning("ModService", "ResolveVersion",
                $"No hay versión estable de la dependencia para Minecraft {mcVersion} ({loader}); se usará la última versión disponible.");
            resolved = FindBestVersion(doc, mcVersion, searchMcVersion, loader, projectId, preferRelease: false, projectId);
        }

        return resolved;
    }

    private static bool StartsWithModSlug(string fileName, string slug)
    {
        if (string.IsNullOrWhiteSpace(slug)) return false;
        return fileName.Equals(slug, StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith(slug + "-", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith(slug + "_", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith(slug + ".", StringComparison.OrdinalIgnoreCase);
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
            "neoforge" => 6,
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
            if (!matchesMc && profile.MinecraftVersion.Equals("latest", StringComparison.OrdinalIgnoreCase))
                matchesMc = true;
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

    public async Task<List<Mod>> GetRecommendedModsAsync()
    {
        _logService.Info("ModService", "GetRecommended", "Cargando mods recomendados...");
        var results = new List<Mod>();

        foreach (var projectId in RecommendedModProjectIds)
        {
            try
            {
                EnsureUserAgent();
                var response = await _httpClient.GetAsync($"https://api.modrinth.com/v2/project/{projectId}");
                response.EnsureSuccessStatusCode();

                using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
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
        "hvFnDODi", // LazyDFU
    };

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

        if (File.Exists(destPath) || File.Exists(destPath + ".disabled"))
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

        if (mod.Status == ModStatus.Active)
        {
            var disabledPath = mod.FilePath + ".disabled";
            if (File.Exists(mod.FilePath))
            {
                File.Move(mod.FilePath, disabledPath);
                mod.FilePath = disabledPath;
            }
            mod.Status = ModStatus.Inactive;
        }
        else
        {
            var activePath = mod.FilePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                ? mod.FilePath[..^".disabled".Length]
                : mod.FilePath;
            if (!string.Equals(mod.FilePath, activePath, StringComparison.OrdinalIgnoreCase)
                && File.Exists(mod.FilePath))
            {
                File.Move(mod.FilePath, activePath);
                mod.FilePath = activePath;
            }
            mod.Status = ModStatus.Active;
        }

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
                using var reader = new StreamReader(mcmodEntry.Open(), Encoding.UTF8);
                var content = await reader.ReadToEndAsync();
                return ParseMcmodInfo(content);
            }

            var fabricEntry = archive.Entries.FirstOrDefault(e =>
                e.FullName.StartsWith("fabric.mod.json", StringComparison.OrdinalIgnoreCase));
            if (fabricEntry != null)
            {
                using var reader = new StreamReader(fabricEntry.Open(), Encoding.UTF8);
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

    public async Task<ModCompatibilityResult> CheckAndDisableIncompatibleModsAsync(int profileId, string targetMcVersion)
    {
        var profile = await _profileRepository.GetByIdAsync(profileId)
            ?? throw new Exception($"Profile {profileId} not found");

        var modsDir = await GetModsFolderAsync(profileId);
        if (!Directory.Exists(modsDir))
            return new ModCompatibilityResult { Checked = 0, Disabled = new List<string>(), Errors = new List<string>() };

        var allMods = Directory.GetFiles(modsDir, "*.jar");
        var disabled = new List<string>();
        var errors = new List<string>();
        var checkedCount = 0;

        foreach (var modFile in allMods)
        {
            try
            {
                checkedCount++;
                var mcVersion = await ExtractMcVersionFromModAsync(modFile);
                
                if (mcVersion != null && !IsVersionCompatible(mcVersion, targetMcVersion))
                {
                    var fileName = Path.GetFileName(modFile);
                    var disabledPath = modFile + ".disabled";
                    
                    if (File.Exists(disabledPath)) File.Delete(disabledPath);
                    File.Move(modFile, disabledPath);
                    
                    disabled.Add($"{fileName} (mod MC: {mcVersion}, target: {targetMcVersion})");
                    _logService.Warning("ModService", "CompatibilityCheck", 
                        $"Disabled incompatible mod: {fileName} (mod MC: {mcVersion}, target: {targetMcVersion})");
                }
            }
            catch (Exception ex)
            {
                var err = $"Error checking {Path.GetFileName(modFile)}: {ex.Message}";
                errors.Add(err);
                _logService.Error("ModService", "CompatibilityCheck", err, ex);
            }
        }

        if (disabled.Count > 0 || errors.Count > 0)
        {
            _logService.Info("ModService", "CompatibilityCheck", 
                $"Compatibility check complete: {checkedCount} checked, {disabled.Count} disabled, {errors.Count} errors");
        }

        return new ModCompatibilityResult
        {
            Checked = checkedCount,
            Disabled = disabled,
            Errors = errors
        };
    }

    public async Task<List<ModUpdateInfo>> CheckForUpdatesAsync(int profileId, string mcVersion)
    {
        var mods = await _modRepository.GetByProfileIdAsync(profileId);
        // Reset update status for all mods
        foreach (var mod in mods)
        {
            mod.LatestVersion = null;
            mod.HasUpdate = false;
        }
        foreach (var mod in mods)
        {
            await _modRepository.UpdateAsync(mod);
        }
        var modrinthVersion = ToModrinthVersion(mcVersion);
        var updates = new List<ModUpdateInfo>();

        foreach (var mod in mods.Where(m => m.Status == ModStatus.Active && !string.IsNullOrEmpty(m.RemoteProjectId)))
        {
            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("ShoroCraftLauncher/1.5");
                var url = $"https://api.modrinth.com/v2/project/{mod.RemoteProjectId}/version?game_versions=%5B%22{modrinthVersion}%22%5D&loaders=%5B%22fabric%22%5D";
                var response = await http.GetAsync(url);
                if (!response.IsSuccessStatusCode) continue;

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.ValueKind.Equals(JsonValueKind.Array)) continue;

                var versions = doc.RootElement.EnumerateArray().ToList();
                if (versions.Count == 0) continue;

                var best = versions.FirstOrDefault(v =>
                    v.TryGetProperty("version_type", out var vt) && vt.GetString() == "release");
                if (best.ValueKind.Equals(JsonValueKind.Undefined))
                    best = versions[0];

                var latestVersion = "";
                if (best.TryGetProperty("version_number", out var vn))
                    latestVersion = vn.GetString() ?? "";

                if (!string.IsNullOrEmpty(latestVersion) && !latestVersion.Equals(mod.ModVersion, StringComparison.OrdinalIgnoreCase))
                {
                    mod.LatestVersion = latestVersion;
                    mod.HasUpdate = true;
                    await _modRepository.UpdateAsync(mod);
                    updates.Add(new ModUpdateInfo(
                        mod.Id, mod.Name, mod.ModVersion, latestVersion,
                        mod.SourceProvider ?? "modrinth", mod.RemoteProjectId, mod.RemoteSlug));
                }
            }
            catch
            {
                // Skip mods that fail update check
            }
        }

        if (updates.Count > 0)
            _logService.Info("ModService", "UpdateCheck", $"Found {updates.Count} mod(s) with updates available.");

        return updates;
    }

    public async Task<Mod?> UpdateModAsync(int modId, string mcVersion)
    {
        var mod = await _modRepository.GetByIdAsync(modId);
        if (mod == null || string.IsNullOrEmpty(mod.RemoteProjectId)) return null;

        _logService.Info("ModService", "UpdateMod", $"Updating '{mod.Name}'...");

        var profile = await _profileRepository.GetByIdAsync(mod.ProfileId)
            ?? throw new Exception($"Profile {mod.ProfileId} not found");

        var searchResult = new Mod
        {
            Name = mod.Name,
            FileName = mod.RemoteProjectId,
            IconPath = mod.IconPath,
            Description = mod.Description
        };

        var updated = await InstallFromSearchAsync(mod.ProfileId, searchResult, mod.SourceProvider ?? "modrinth");
        _logService.Info("ModService", "UpdateMod", $"'{mod.Name}' updated to {updated.ModVersion}.");
        return updated;
    }

    private async Task<string?> ExtractMcVersionFromModAsync(string modPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(modPath);
            
            // Try fabric.mod.json first (Fabric mods)
            var fabricEntry = archive.Entries.FirstOrDefault(e => 
                e.FullName.Equals("fabric.mod.json", StringComparison.OrdinalIgnoreCase));
            if (fabricEntry != null)
            {
                using var reader = new StreamReader(fabricEntry.Open(), Encoding.UTF8);
                var content = await reader.ReadToEndAsync();
                var (_, mcVersion, _) = ParseFabricModJson(content);
                if (!string.IsNullOrEmpty(mcVersion)) return mcVersion;
            }

            // Try mcmod.info (Forge/FML mods)
            var mcmodEntry = archive.Entries.FirstOrDefault(e => 
                e.FullName.Equals("mcmod.info", StringComparison.OrdinalIgnoreCase));
            if (mcmodEntry != null)
            {
                using var reader = new StreamReader(mcmodEntry.Open(), Encoding.UTF8);
                var content = await reader.ReadToEndAsync();
                var (_, mcVersion, _) = ParseMcmodInfo(content);
                if (!string.IsNullOrEmpty(mcVersion)) return mcVersion;
            }
        }
        catch { }

        // Fallback: extract MC version from filename pattern (e.g., "iris-fabric-1.11.3+mc26.1.2.jar")
        try
        {
            var fileName = Path.GetFileNameWithoutExtension(modPath);
            var mcIndex = fileName.LastIndexOf("+mc", StringComparison.OrdinalIgnoreCase);
            if (mcIndex >= 0)
            {
                var mcVer = fileName.Substring(mcIndex + 3);
                // Take only the version part (stop at next non-version char)
                var match = System.Text.RegularExpressions.Regex.Match(mcVer, @"^[\d.]+");
                if (match.Success) return match.Value;
            }
        }
        catch { }

        return null;
    }

    private static bool IsVersionCompatible(string modMcVersion, string targetMcVersion)
    {
        if (string.IsNullOrEmpty(modMcVersion) || string.IsNullOrEmpty(targetMcVersion))
            return true; // Unknown = assume compatible

        // Normalize both versions
        var modVersion = NormalizeVersion(modMcVersion);
        var targetVersion = NormalizeVersion(targetMcVersion);

        // Exact match
        if (modVersion.Equals(targetVersion, StringComparison.OrdinalIgnoreCase))
            return true;

        // Handle wildcard "*"
        if (modVersion.Contains("*")) return true;

        // For MC 26.x versions (new versioning): require major.minor match
        // 26.1 ≠ 26.2 (they are distinct releases, not sub-versions)
        if (modVersion.StartsWith("1.21.", StringComparison.OrdinalIgnoreCase) &&
            targetVersion.StartsWith("1.21.", StringComparison.OrdinalIgnoreCase))
        {
            var modParts = modVersion.Split('.');
            var targetParts = targetVersion.Split('.');
            // Compare major.minor only (first 2 parts)
            if (modParts.Length >= 2 && targetParts.Length >= 2)
                return modParts[0] == targetParts[0] && modParts[1] == targetParts[1];
        }

        // Handle version ranges like "1.21" matching "1.21.1"
        var modPartsLegacy = modVersion.Split('.');
        var targetPartsLegacy = targetVersion.Split('.');

        // If mod version is a prefix of target (e.g., mod "1.21" targets "1.21.1")
        if (modPartsLegacy.Length <= targetPartsLegacy.Length)
        {
            bool prefixMatch = true;
            for (int i = 0; i < modPartsLegacy.Length; i++)
            {
                if (!modPartsLegacy[i].Equals(targetPartsLegacy[i], StringComparison.OrdinalIgnoreCase))
                {
                    prefixMatch = false;
                    break;
                }
            }
            if (prefixMatch) return true;
        }

        return false;
    }

    private static string NormalizeVersion(string version)
    {
        if (string.IsNullOrEmpty(version)) return version;
        
        var trimmed = version.Trim();
        // Handle "26.X" or "26.X.Y" format - convert to Modrinth format for comparison
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
