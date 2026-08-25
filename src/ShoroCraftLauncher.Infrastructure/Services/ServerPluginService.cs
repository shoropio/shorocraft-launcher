using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Infrastructure.Downloading;

namespace ShoroCraftLauncher.Infrastructure.Services;

public class ServerPluginService : IServerPluginService
{
    private const string GeyserDownloadBaseUrl = "https://download.geysermc.org/v2/projects";
    private const string UserAgent = "ShoroCraftLauncher/1.6.5 (https://github.com/Shoropio/shorocraft-launcher)";
    private static readonly List<(string ProjectId, string DisplayName, string Keyword, string JarName)> KnownPlugins = new()
    {
        ("geyser", "GeyserMC", "geyser", "geyser-spigot.jar"),
        ("floodgate", "Floodgate", "floodgate", "floodgate-spigot.jar")
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

        var known = KnownPlugins.FirstOrDefault(k => k.ProjectId == plugin.ProjectId);
        if (known.ProjectId == null)
            throw new InvalidOperationException("El plugin no tiene un origen conocido para instalar.");

        var pluginsDir = Path.Combine(server.DirectoryPath, "plugins");
        Directory.CreateDirectory(pluginsDir);

        var (url, fileName) = await ResolveDownloadAsync(plugin.ProjectId).ConfigureAwait(false);
        var destPath = Path.Combine(pluginsDir, fileName);

        foreach (var existing in Directory.GetFiles(pluginsDir, "*.jar*"))
        {
            if (string.Equals(existing, destPath, StringComparison.OrdinalIgnoreCase))
                continue;
            var baseName = Path.GetFileNameWithoutExtension(existing);
            if (MatchKnownProjectId(baseName) == plugin.ProjectId)
            {
                try { File.Delete(existing); }
                catch (Exception ex) { _logger.LogWarning(ex, "No se pudo eliminar el archivo previo {File}", existing); }
            }
        }

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
        var known = KnownPlugins.FirstOrDefault(k => k.ProjectId == projectId);
        if (known.ProjectId == null)
            throw new InvalidOperationException($"El plugin '{projectId}' no tiene un origen conocido para instalar.");

        var version = await GetLatestVersionAsync(projectId).ConfigureAwait(false) ?? "latest";
        var url = $"{GeyserDownloadBaseUrl}/{projectId}/versions/latest/builds/latest/downloads/spigot";
        var fileName = known.JarName.Replace(".jar", $"-{version}.jar");

        return (url, fileName);
    }

    private async Task<string?> GetLatestVersionAsync(string projectId)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{GeyserDownloadBaseUrl}/{projectId}/versions/latest");
            request.Headers.UserAgent.ParseAdd(UserAgent);
            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("version", out var v))
                return v.GetString();
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
