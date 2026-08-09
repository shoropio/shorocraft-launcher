using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Infrastructure.Services;

public class ServerService : IServerService
{
    private const string PaperApiBaseUrl = "https://api.papermc.io/v2/projects/paper";
    private const string ServerJarName = "server.jar";
    private const int MaxLogLines = 2000;

    private readonly IServerRepository _repository;
    private readonly IMinecraftService _minecraftService;
    private readonly IJavaService _javaService;
    private readonly ILogger<ServerService> _logger;
    private readonly ILogService? _logService;
    private readonly HttpClient _httpClient;

    private readonly List<MinecraftServer> _servers = new();
    private readonly Dictionary<int, Process> _processes = new();
    private readonly Dictionary<int, List<string>> _logHistory = new();
    private readonly object _lock = new();

    public ServerService(
        IServerRepository repository,
        IMinecraftService minecraftService,
        IJavaService javaService,
        HttpClient httpClient,
        ILogger<ServerService> logger,
        ILogService? logService = null)
    {
        _repository = repository;
        _minecraftService = minecraftService;
        _javaService = javaService;
        _httpClient = httpClient;
        _logger = logger;
        _logService = logService;
    }

    public IReadOnlyList<MinecraftServer> Servers
    {
        get { lock (_lock) return _servers.ToList(); }
    }

    public event Action? ServersChanged;
    public event Action<string>? LogOutput;
    public event Action<double, string>? ProgressChanged;
    public event Action<ServerStatus>? StatusChanged;

    public async Task LoadAsync()
    {
        var servers = await _repository.GetAllAsync();
        lock (_lock)
        {
            _servers.Clear();
            _servers.AddRange(servers);
        }
        ServersChanged?.Invoke();
    }

    public async Task<List<string>> GetAvailableVanillaVersionsAsync()
    {
        var versions = await _minecraftService.FetchAvailableVersionsAsync();
        return versions
            .Where(v => v.VersionType == "release")
            .Select(v => v.VersionId)
            .ToList();
    }

    public async Task<List<string>> GetAvailablePaperVersionsAsync()
    {
        try
        {
            var json = await _httpClient.GetStringAsync(PaperApiBaseUrl);
            var doc = JsonDocument.Parse(json);
            var versions = new List<string>();
            if (doc.RootElement.TryGetProperty("versions", out var versionsProp))
            {
                foreach (var v in versionsProp.EnumerateArray())
                    versions.Add(v.GetString() ?? string.Empty);
            }
            return versions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Paper versions");
            return new List<string>();
        }
    }

    public async Task<MinecraftServer> CreateServerAsync(string name, ServerType type, string minecraftVersion, int maxRamMB, string? worldName = null)
    {
        _logService?.Info("ServerService", "Create", $"Creando servidor '{name}' ({type} {minecraftVersion})...");

        var safeName = SanitizeFolderName(name);
        var directoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShoroCraftLauncher", "servers", safeName);

        Directory.CreateDirectory(directoryPath);
        WriteEula(directoryPath);
        WriteServerProperties(directoryPath, worldName);

        var server = new MinecraftServer
        {
            Name = name,
            Type = type,
            MinecraftVersion = minecraftVersion,
            DirectoryPath = directoryPath,
            MinRamMB = 1024,
            MaxRamMB = maxRamMB,
            WorldName = string.IsNullOrWhiteSpace(worldName) ? "world" : worldName,
            Status = ServerStatus.Stopped
        };

        server.Id = await _repository.CreateAsync(server);

        lock (_lock)
        {
            _servers.Add(server);
        }
        ServersChanged?.Invoke();
        _logService?.Info("ServerService", "Created", $"Servidor '{name}' creado en {directoryPath}.");
        return server;
    }

    public async Task DeleteServerAsync(MinecraftServer server)
    {
        await StopAsync(server);

        _logService?.Info("ServerService", "Delete", $"Eliminando servidor '{server.Name}'...");

        try
        {
            if (Directory.Exists(server.DirectoryPath))
                Directory.Delete(server.DirectoryPath, true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete server directory");
        }

        await _repository.DeleteAsync(server.Id);

        lock (_lock)
        {
            _servers.RemoveAll(s => s.Id == server.Id);
            _logHistory.Remove(server.Id);
        }
        ServersChanged?.Invoke();
        _logService?.Info("ServerService", "Deleted", $"Servidor '{server.Name}' eliminado.");
    }

    public async Task<ServerLaunchResult> StartAsync(MinecraftServer server)
    {
        if (IsRunning(server))
            return new ServerLaunchResult { Success = false, ErrorMessage = "El servidor ya está en ejecución." };

        try
        {
            Directory.CreateDirectory(server.DirectoryPath);
            if (!File.Exists(Path.Combine(server.DirectoryPath, "eula.txt")))
                WriteEula(server.DirectoryPath);

            var jarPath = await EnsureServerJarAsync(server);

            var javaPath = server.JavaPath;
            if (string.IsNullOrEmpty(javaPath))
            {
                _logService?.Info("Java", "ResolveStarted", "Buscando Java recomendado para el servidor...", new { server.MinecraftVersion });
                javaPath = await _javaService.GetRecommendedJavaPathAsync(server.MinecraftVersion);
                if (string.IsNullOrEmpty(javaPath))
                {
                    Log($"Descargando Java necesario para el servidor...");
                    javaPath = await _javaService.DownloadJavaForVersionAsync(
                        server.MinecraftVersion,
                        new Progress<double>(pct =>
                        {
                            var msg = $"Descargando Java necesario... {pct:0}%";
                            Log($"[INFO] {msg}");
                            ProgressChanged?.Invoke(pct, msg);
                        }));
                }

                if (string.IsNullOrEmpty(javaPath))
                    return new ServerLaunchResult { Success = false, ErrorMessage = "No se pudo encontrar ni descargar Java. Revisa tu conexión." };

                server.JavaPath = javaPath;
                await _repository.UpdateAsync(server);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = javaPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = server.DirectoryPath,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            startInfo.ArgumentList.Add($"-Xms{server.MinRamMB}M");
            startInfo.ArgumentList.Add($"-Xmx{server.MaxRamMB}M");
            startInfo.ArgumentList.Add("-jar");
            startInfo.ArgumentList.Add(jarPath);
            startInfo.ArgumentList.Add("nogui");

            _logger.LogInformation("Starting server {ServerName} with Java {JavaPath}", server.Name, javaPath);
            LogServer(server.Id, $"[INFO] Iniciando servidor '{server.Name}'...");

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    _logger.LogDebug("Server {ServerName}: {Line}", server.Name, e.Data);
                    LogServer(server.Id, e.Data);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    _logger.LogWarning("Server {ServerName}: {Line}", server.Name, e.Data);
                    LogServer(server.Id, $"[ERROR] {e.Data}");
                }
            };

            process.Exited += (s, _) =>
            {
                var p = (Process)s!;
                _logger.LogInformation("Server {ServerName} exited with code {ExitCode}", server.Name, p.ExitCode);
                LogServer(server.Id, $"El servidor '{server.Name}' terminó con código {p.ExitCode}.");
                CleanupProcess(server.Id);
                SetStatus(server, ServerStatus.Stopped);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            lock (_lock)
            {
                _processes[server.Id] = process;
                if (!_logHistory.ContainsKey(server.Id))
                    _logHistory[server.Id] = new List<string>();
            }

            SetStatus(server, ServerStatus.Running);
            LogServer(server.Id, $"[INFO] Servidor '{server.Name}' en ejecución (PID {process.Id}).");

            return new ServerLaunchResult { Success = true, ProcessId = process.Id };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start server {ServerName}", server.Name);
            _logService?.Error("ServerService", "StartFailed", "Error al iniciar el servidor.", ex);
            SetStatus(server, ServerStatus.Error);
            return new ServerLaunchResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task StopAsync(MinecraftServer server)
    {
        Process? process;
        lock (_lock)
        {
            _processes.TryGetValue(server.Id, out process);
        }

        if (process is { HasExited: false })
        {
            _logger.LogInformation("Stopping server {ServerName}", server.Name);
            _logService?.Info("ServerService", "Stop", $"Deteniendo servidor '{server.Name}'...");
            LogServer(server.Id, $"[INFO] Enviando 'stop' a '{server.Name}'...");
            SetStatus(server, ServerStatus.Stopping);

            try
            {
                process.StandardInput.WriteLine("stop");
                process.StandardInput.Flush();
                if (!process.WaitForExit(15000))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to stop server gracefully, killing process");
                try { process.Kill(entireProcessTree: true); } catch { }
            }

            CleanupProcess(server.Id);
            SetStatus(server, ServerStatus.Stopped);
            LogServer(server.Id, $"[INFO] Servidor '{server.Name}' detenido.");
        }
    }

    public Task SendCommandAsync(MinecraftServer server, string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return Task.CompletedTask;

        Process? process;
        lock (_lock)
        {
            _processes.TryGetValue(server.Id, out process);
        }

        if (process is { HasExited: false })
        {
            try
            {
                process.StandardInput.WriteLine(command);
                process.StandardInput.Flush();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send command to server {ServerName}", server.Name);
            }
        }

        return Task.CompletedTask;
    }

    public bool IsRunning(MinecraftServer server)
    {
        lock (_lock)
        {
            return _processes.TryGetValue(server.Id, out var process)
                && process is { HasExited: false };
        }
    }

    public IReadOnlyList<string> GetLogHistory(MinecraftServer server)
    {
        lock (_lock)
        {
            if (_logHistory.TryGetValue(server.Id, out var history))
                return history.ToList();
        }
        return Array.Empty<string>();
    }

    private async Task<string> EnsureServerJarAsync(MinecraftServer server)
    {
        var jarPath = Path.Combine(server.DirectoryPath, ServerJarName);
        if (File.Exists(jarPath)) return jarPath;

        _logService?.Info("ServerService", "DownloadJar", $"Descargando {server.Type} {server.MinecraftVersion}...");
        LogServer(server.Id, $"[INFO] Descargando jar del servidor ({server.Type} {server.MinecraftVersion})...");

        var url = server.Type == ServerType.Paper
            ? await ResolvePaperJarUrlAsync(server.MinecraftVersion)
            : await _minecraftService.GetServerJarUrlAsync(server.MinecraftVersion);

        if (string.IsNullOrEmpty(url))
            throw new Exception($"No se encontró el jar para {server.Type} {server.MinecraftVersion}.");

        var downloadPath = jarPath + ".tmp";
        using (var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = File.Create(downloadPath);

            var buffer = new byte[81920];
            long readTotal = 0;
            int read;
            while ((read = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read));
                readTotal += read;
                if (totalBytes > 0)
                {
                    var pct = (double)readTotal / totalBytes * 100;
                    var msg = $"Descargando jar del servidor... {pct:0}%";
                    LogServer(server.Id, $"[INFO] {msg}");
                    ProgressChanged?.Invoke(pct, msg);
                }
            }
        }

        File.Move(downloadPath, jarPath, true);
        LogServer(server.Id, $"[INFO] Jar del servidor descargado.");
        return jarPath;
    }

    private async Task<string?> ResolvePaperJarUrlAsync(string minecraftVersion)
    {
        var buildsUrl = $"{PaperApiBaseUrl}/versions/{minecraftVersion}/builds";
        var json = await _httpClient.GetStringAsync(buildsUrl);
        var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("builds", out var builds) || builds.GetArrayLength() == 0)
            return null;

        JsonElement latest = builds[builds.GetArrayLength() - 1];
        var build = latest.GetProperty("build").GetInt32();

        if (!latest.TryGetProperty("downloads", out var downloads)
            || !downloads.TryGetProperty("application", out var application))
            return null;

        return $"{PaperApiBaseUrl}/versions/{minecraftVersion}/builds/{build}/downloads/paper-{minecraftVersion}-{build}.jar";
    }

    private void SetStatus(MinecraftServer server, ServerStatus status)
    {
        lock (_lock)
        {
            var existing = _servers.FirstOrDefault(s => s.Id == server.Id);
            if (existing != null)
                existing.Status = status;
            server.Status = status;
        }
        ServersChanged?.Invoke();
        StatusChanged?.Invoke(status);
    }

    private void CleanupProcess(int serverId)
    {
        lock (_lock)
        {
            _processes.Remove(serverId);
        }
    }

    private void Log(string message)
    {
        LogOutput?.Invoke(message);
    }

    private void LogServer(int serverId, string message)
    {
        lock (_lock)
        {
            if (_logHistory.TryGetValue(serverId, out var history))
            {
                history.Add(message);
                if (history.Count > MaxLogLines)
                    history.RemoveRange(0, history.Count - MaxLogLines);
            }
        }
        LogOutput?.Invoke(message);
    }

    private static void WriteEula(string directoryPath)
    {
        File.WriteAllText(Path.Combine(directoryPath, "eula.txt"),
            "#EULA aceptada automáticamente por ShoroCraft Launcher\n"
            + "eula=true\n");
    }

    private static void WriteServerProperties(string directoryPath, string? worldName)
    {
        var levelName = string.IsNullOrWhiteSpace(worldName) ? "world" : worldName;
        File.WriteAllText(Path.Combine(directoryPath, "server.properties"),
            "#Minecraft server properties\n"
            + "server-port=25565\n"
            + $"level-name={levelName}\n"
            + "motd=A ShoroCraft server\n"
            + "online-mode=false\n"
            + "max-players=20\n"
            + "view-distance=10\n");
    }

    private static string SanitizeFolderName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "server" : name;
    }
}
