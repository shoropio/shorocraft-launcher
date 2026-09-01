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
    public async Task<Mod> InstallFromSearchAsync(int profileId, Mod searchResult, string provider)
    {
        var profile = await _profileRepository.GetByIdAsync(profileId).ConfigureAwait(false)
            ?? throw new Exception($"Profile {profileId} not found");

        _logService.Info("ModService", "InstallFromSearch", $"Instalando '{searchResult.Name}' desde {provider}...");

        var modsDir = await GetModsFolderAsync(profileId).ConfigureAwait(false);
        Directory.CreateDirectory(modsDir);

        string downloadUrl;
        string fileName;
        string modVersion;
        long fileSize;
        string projectSlug = string.Empty;
        IReadOnlyList<ModrinthDependency>? dependencies = null;

        if (provider.Equals("CurseForge", StringComparison.OrdinalIgnoreCase))
            (downloadUrl, fileName, modVersion, fileSize) = await ResolveCurseForgeDownloadAsync(searchResult, profile).ConfigureAwait(false);
        else
        {
            var resolved = await ResolveModrinthDownloadAsync(searchResult, profile).ConfigureAwait(false);
            downloadUrl = resolved.Url;
            fileName = resolved.FileName;
            modVersion = resolved.Version;
            fileSize = resolved.Size;
            dependencies = resolved.Dependencies;
            projectSlug = resolved.ProjectSlug;
        }

        fileName = DownloadPathGuard.SafeFileName(fileName);

        var destPath = Path.Combine(modsDir, fileName);
        var tempPath = destPath + ".tmp";

        // Localiza una instalación previa del mismo mod, pero NO la borra todavía:
        // si la descarga falla, el mod original queda intacto.
        var existingMods = await _modRepository.GetByProfileIdAsync(profileId).ConfigureAwait(false);
        var existing = string.IsNullOrWhiteSpace(projectSlug)
            ? existingMods.FirstOrDefault(m => string.Equals(m.FileName, fileName, StringComparison.OrdinalIgnoreCase))
            : existingMods.FirstOrDefault(m => StartsWithModSlug(m.FileName, projectSlug));

        _logger.LogInformation("Downloading mod from {Url}", downloadUrl);
        _logService.Info("ModService", "DownloadMod", $"Descargando {fileName} ({FormatFileSize(fileSize)})...");
        try
        {
            await _resumableDownloadService.DownloadAsync(downloadUrl, tempPath).ConfigureAwait(false);
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
            await _modRepository.DeleteAsync(existing.Id).ConfigureAwait(false);
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

        await _modRepository.CreateAsync(mod).ConfigureAwait(false);
        _logger.LogInformation("Mod {Name} installed from {Provider}", mod.Name, provider);
        _logService.Info("ModService", "InstallFromSearch", $"'{searchResult.Name}' instalado correctamente.");

        if (dependencies is { Count: > 0 })
            await InstallRequiredDependenciesAsync(profileId, modsDir, profile, dependencies).ConfigureAwait(false);

        return mod;
    }

    private async Task<ModrinthVersionInfo> ResolveModrinthDownloadAsync(Mod searchResult, Profile profile)
    {
        var projectId = searchResult.FileName;
        var projectSlug = await GetProjectSlugAsync(projectId).ConfigureAwait(false);

        using var request = CreateModrinthRequest($"https://api.modrinth.com/v2/project/{projectId}/version");
        await ModrinthApiRateLimiter.WaitAsync().ConfigureAwait(false);
        var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
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
        => (await GetProjectInfoAsync(projectId).ConfigureAwait(false)).slug;

    private async Task<(string slug, string title, string icon)> GetProjectInfoAsync(string projectId)
    {
        try
        {
            using var request = CreateModrinthRequest($"https://api.modrinth.com/v2/project/{projectId}");
            await ModrinthApiRateLimiter.WaitAsync().ConfigureAwait(false);
            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            var root = doc.RootElement;
            var slug = root.TryGetProperty("slug", out var s) ? s.GetString() ?? projectId : projectId;
            var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? projectId : projectId;
            var icon = root.TryGetProperty("icon_url", out var i) ? i.GetString() ?? string.Empty : string.Empty;
            return (slug, title, icon);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch project info for {ProjectId}", projectId);
            return (projectId, projectId, string.Empty);
        }
    }

    private HttpRequestMessage CreateModrinthRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        return request;
    }

    private async Task InstallRequiredDependenciesAsync(int profileId, string modsDir, Profile profile, IReadOnlyList<ModrinthDependency> dependencies)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await InstallDependenciesRecursiveAsync(profileId, modsDir, profile, dependencies, visited).ConfigureAwait(false);
    }

    private async Task InstallDependenciesRecursiveAsync(int profileId, string modsDir, Profile profile, IReadOnlyList<ModrinthDependency> dependencies, HashSet<string> visited)
    {
        foreach (var dep in dependencies.Where(d => d.DependencyType.Equals("required", StringComparison.OrdinalIgnoreCase)))
            await InstallDependencyAsync(profileId, modsDir, profile, dep, visited).ConfigureAwait(false);
    }

    private async Task InstallDependencyAsync(int profileId, string modsDir, Profile profile, ModrinthDependency dependency, HashSet<string> visited)
    {
        if (dependency.DependencyType.Equals("incompatible", StringComparison.OrdinalIgnoreCase))
            return;

        if (!visited.Add(dependency.ProjectId))
            return;

        var (slug, title, icon) = await GetProjectInfoAsync(dependency.ProjectId).ConfigureAwait(false);
        var resolved = await ResolveDependencyVersionAsync(dependency.ProjectId, dependency.VersionId, profile).ConfigureAwait(false);
        if (resolved == null)
        {
            _logService.Warning("ModService", "InstallDependency",
                $"No se encontró una versión compatible de la dependencia '{title}' para Minecraft {profile.MinecraftVersion} ({profile.Type}).");
            return;
        }

        var safeDependencyFileName = DownloadPathGuard.SafeFileName(resolved.FileName);
        var destPath = Path.Combine(modsDir, safeDependencyFileName);

        if (File.Exists(destPath) || File.Exists(destPath + ".disabled"))
        {
            _logService.Info("ModService", "InstallDependency", $"La dependencia '{title}' ya está instalada ({resolved.FileName}).");
            await InstallDependenciesRecursiveAsync(profileId, modsDir, profile, resolved.Dependencies, visited).ConfigureAwait(false);
            return;
        }

        var existing = (await _modRepository.GetByProfileIdAsync(profileId).ConfigureAwait(false))
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
            await _modRepository.DeleteAsync(existing.Id).ConfigureAwait(false);
            _logService.Info("ModService", "InstallDependency",
                $"Reemplazando '{existing.FileName}' por la versión requerida '{resolved.FileName}'.");
        }

        _logService.Info("ModService", "InstallDependency", $"Instalando dependencia '{title}' ({resolved.FileName})...");
        await _resumableDownloadService.DownloadAsync(resolved.Url, destPath).ConfigureAwait(false);
        var dependencyBytes = new FileInfo(destPath).Length;

        var mod = new Mod
        {
            ProfileId = profileId,
            Name = title,
            FileName = safeDependencyFileName,
            FilePath = destPath,
            FileSizeBytes = resolved.Size > 0 ? resolved.Size : dependencyBytes,
            MinecraftVersion = profile.MinecraftVersion,
            ModVersion = resolved.Version,
            IconPath = icon,
            Description = "Instalado automáticamente como dependencia.",
            Status = ModStatus.Active
        };
        await _modRepository.CreateAsync(mod).ConfigureAwait(false);
        _logService.Info("ModService", "InstallDependency", $"Dependencia '{title}' instalada correctamente.");

        await InstallDependenciesRecursiveAsync(profileId, modsDir, profile, resolved.Dependencies, visited).ConfigureAwait(false);
    }

    private async Task<ModrinthVersionInfo?> ResolveDependencyVersionAsync(string projectId, string versionId, Profile profile)
    {
        using var request = CreateModrinthRequest($"https://api.modrinth.com/v2/project/{projectId}/version");
        await ModrinthApiRateLimiter.WaitAsync().ConfigureAwait(false);
        var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
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
        var apiKey = await _settingsRepository.GetAsync("curseforge_api_key").ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Configura una API key de CurseForge en Configuración.");

        var modId = searchResult.FileName;
        var url = $"https://api.curseforge.com/v1/mods/{modId}/files?pageSize=1&sortField=1&sortOrder=desc";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.UserAgent.ParseAdd("ShoroCraftLauncher/1.0.0");

        await CurseForgeApiRateLimiter.WaitAsync().ConfigureAwait(false);
        using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = System.Text.Json.JsonDocument.Parse(json);

        var loaderTypeId = profile.Type.ToString().ToLowerInvariant() switch
        {
            "forge" => 1,
            "neoforge" => 6,
            "fabric" => 4,
            "quilt" => 5,
            _ => 0
        };

        var curseForgeVersion = MinecraftVersions.ToModrinthVersion(profile.MinecraftVersion);

        foreach (var file in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            var gameVersions = file.GetProperty("gameVersions");
            bool matchesMc = false;
            foreach (var gv in gameVersions.EnumerateArray())
            {
                if (gv.GetString() == profile.MinecraftVersion
                    || gv.GetString() == curseForgeVersion) { matchesMc = true; break; }
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

}
