using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Infrastructure.Downloading;

namespace ShoroCraftLauncher.Infrastructure.Services;

public class ServerPluginService : IServerPluginService
{
    private static readonly string ModrinthApiBaseUrl = "https://api.modrinth.com/v2";
    private static readonly List<(string ProjectId, string DisplayName, string Keyword)> KnownPlugins = new()
    {
        ("geyser", "GeyserMC", "geyser"),
        ("floodgate", "Floodgate", "floodgate")
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<ServerPluginService> _logger;
    private readonly ILogService? _logService;
    private readonly IResumableDownloadService _resumableDownloadService;

    public ServerPluginService(
        HttpClient httpClient,
        ILogger<ServerPluginService> logger,
        ILogService? logService = null,
        IResumableDownloadService? resumableDownloadService = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _logService = logService;
        _resumableDownloadService = resumableDownloadService ?? new ResumableDownloadService(httpClient);
    }

    public async Task<List<ServerPlugin>> GetPluginsAsync(MinecraftServer server)
    {
        var pluginsDir = Path.Combine(server.DirectoryPath, "plugins");
        var result = new List<ServerPlugin>();
        var installedByProject = new Dictionary<string, ServerPlugin>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(pluginsDir))
        {
            foreach (var file in Directory.GetFiles(pluginsDir, "*.jar*"))
            {
                var name = Path.GetFileName(file);
                var isDisabled = name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                var baseName = isDisabled
                    ? name.Substring(0, name.Length - ".disabled".Length)
                    : name;

                var projectId = MatchKnownProjectId(baseName);
                var plugin = new ServerPlugin
                {
                    Name = PrettyName(baseName, projectId),
                    FileName = name,
                    FilePath = file,
                    Version = ParseVersion(baseName),
                    ProjectId = projectId,
                    IsKnown = projectId != null,
                    IsInstalled = true,
                    IsDisabled = isDisabled
                };

                result.Add(plugin);
                if (projectId != null)
                    installedByProject[projectId] = plugin;
            }
        }

        foreach (var known in KnownPlugins)
        {
            if (!installedByProject.ContainsKey(known.ProjectId))
            {
                result.Add(new ServerPlugin
                {
                    Name = known.DisplayName,
                    ProjectId = known.ProjectId,
                    IsKnown = true,
                    IsInstalled = false
                });
            }
        }

        foreach (var known in KnownPlugins)
        {
            if (!installedByProject.TryGetValue(known.ProjectId, out var installed))
                continue;

            try
            {
                var latest = await GetLatestVersionAsync(known.ProjectId).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(latest))
                    installed.HasUpdate = !installed.Version.Equals(latest, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check update for plugin {Project}", known.ProjectId);
            }
        }

        return result;
    }

    public async Task InstallPluginAsync(MinecraftServer server, ServerPlugin plugin)
    {
        if (string.IsNullOrEmpty(plugin.ProjectId))
            throw new InvalidOperationException("El plugin no tiene un origen conocido para instalar.");

        var pluginsDir = Path.Combine(server.DirectoryPath, "plugins");
        Directory.CreateDirectory(pluginsDir);

        var (url, fileName) = await ResolveDownloadAsync(plugin.ProjectId).ConfigureAwait(false);
        var destPath = Path.Combine(pluginsDir, fileName);

        _logService?.Info("ServerPlugin", "Install", $"Descargando plugin '{plugin.Name}' ({fileName})...");
        await _resumableDownloadService.DownloadAsync(url, destPath).ConfigureAwait(false);
        _logService?.Info("ServerPlugin", "Install", $"Plugin '{plugin.Name}' instalado en {destPath}.");
    }

    public Task DeletePluginAsync(MinecraftServer server, ServerPlugin plugin)
    {
        if (File.Exists(plugin.FilePath))
            File.Delete(plugin.FilePath);
        return Task.CompletedTask;
    }

    public Task TogglePluginAsync(MinecraftServer server, ServerPlugin plugin)
    {
        if (!File.Exists(plugin.FilePath))
            return Task.CompletedTask;

        string target;
        if (plugin.IsDisabled)
            target = plugin.FilePath.Substring(0, plugin.FilePath.Length - ".disabled".Length);
        else
            target = plugin.FilePath + ".disabled";

        File.Move(plugin.FilePath, target);
        plugin.FilePath = target;
        plugin.IsDisabled = !plugin.IsDisabled;
        plugin.FileName = Path.GetFileName(target);
        return Task.CompletedTask;
    }

    private async Task<(string Url, string FileName)> ResolveDownloadAsync(string projectId)
    {
        var url = $"{ModrinthApiBaseUrl}/project/{projectId}/version";
        await ModrinthApiRateLimiter.WaitAsync().ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("ShoroCraftLauncher/1.6.5 (https://github.com/Shoropio/shorocraft-launcher)");
        var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var versions = doc.RootElement.EnumerateArray().ToList();
        if (versions.Count == 0)
            throw new InvalidOperationException("No se encontraron versiones del plugin.");

        var best = versions.FirstOrDefault(v =>
            v.TryGetProperty("version_type", out var vt) && vt.GetString() == "release");
        if (best.ValueKind == JsonValueKind.Undefined)
            best = versions[0];

        var files = best.GetProperty("files").EnumerateArray().ToList();
        if (files.Count == 0)
            throw new InvalidOperationException("El plugin no tiene archivos para descargar.");

        var file = files[0];
        var downloadUrl = file.GetProperty("url").GetString()
            ?? throw new InvalidOperationException("URL de descarga no disponible.");
        var fileName = file.TryGetProperty("filename", out var fn) ? fn.GetString() ?? $"{projectId}.jar" : $"{projectId}.jar";

        return (downloadUrl, fileName);
    }

    private async Task<string?> GetLatestVersionAsync(string projectId)
    {
        try
        {
            var url = $"{ModrinthApiBaseUrl}/project/{projectId}/version";
            await ModrinthApiRateLimiter.WaitAsync().ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("ShoroCraftLauncher/1.6.5 (https://github.com/Shoropio/shorocraft-launcher)");
            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var versions = doc.RootElement.EnumerateArray().ToList();
            if (versions.Count == 0) return null;

            var best = versions.FirstOrDefault(v =>
                v.TryGetProperty("version_type", out var vt) && vt.GetString() == "release");
            if (best.ValueKind == JsonValueKind.Undefined)
                best = versions[0];

            if (best.TryGetProperty("version_number", out var vn))
                return vn.GetString();
        }
        catch
        {
            // Best-effort: ignore update check failures.
        }

        return null;
    }

    private static string? MatchKnownProjectId(string fileNameWithoutExt)
    {
        var lower = fileNameWithoutExt.ToLowerInvariant();
        foreach (var known in KnownPlugins)
        {
            if (lower.Contains(known.Keyword))
                return known.ProjectId;
        }
        return null;
    }

    private static string PrettyName(string fileNameWithoutExt, string? projectId)
    {
        if (projectId != null)
        {
            var known = KnownPlugins.FirstOrDefault(k => k.ProjectId == projectId);
            if (known.ProjectId != null)
                return known.DisplayName;
        }

        var name = fileNameWithoutExt;
        var idx = name.IndexOfAny(new[] { '-', '_' });
        if (idx > 0)
            name = name.Substring(0, idx);
        return name;
    }

    private static string ParseVersion(string fileNameWithoutExt)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            fileNameWithoutExt, @"\d+(\.\d+){1,}");
        return match.Success ? match.Value : string.Empty;
    }
}
