using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Infrastructure;
using ShoroCraftLauncher.Infrastructure.Downloading;

namespace ShoroCraftLauncher.Infrastructure.Services;

public class ServerService : IServerService
{
    private const string PaperApiBaseUrl = "https://api.papermc.io/v3/projects/paper";
    private const string PaperUserAgent = "ShoroCraftLauncher/1.6.5 (https://github.com/Shoropio/shorocraft-launcher)";
    private const string ServerJarName = "server.jar";
    private const string ServerPidFileName = "server.pid";
    private const int MaxLogLines = 2000;

    private readonly IServerRepository _repository;
    private readonly IMinecraftService _minecraftService;
    private readonly IJavaService _javaService;
    private readonly ILogger<ServerService> _logger;
    private readonly ILogService? _logService;
    private readonly HttpClient _httpClient;
    private readonly IResumableDownloadService _resumableDownloadService;

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
        ILogService? logService = null,
        IResumableDownloadService? resumableDownloadService = null)
    {
        _repository = repository;
        _minecraftService = minecraftService;
        _javaService = javaService;
        _httpClient = httpClient;
        _logger = logger;
        _logService = logService;
        _resumableDownloadService = resumableDownloadService ?? new ResumableDownloadService(httpClient);
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
        var servers = await _repository.GetAllAsync().ConfigureAwait(false);
        lock (_lock)
        {
            _servers.Clear();
            _servers.AddRange(servers);
        }
        ServersChanged?.Invoke();
    }

    public async Task<List<string>> GetAvailableVanillaVersionsAsync()
    {
        var versions = await _minecraftService.FetchAvailableVersionsAsync().ConfigureAwait(false);
        return versions
            .Where(v => v.VersionType == "release")
            .Select(v => v.VersionId)
            .ToList();
    }

    public async Task<List<string>> GetAvailablePaperVersionsAsync()
    {
        try
        {
            var doc = await GetPaperJsonAsync(PaperApiBaseUrl).ConfigureAwait(false);
            var versions = new List<string>();

            if (doc.RootElement.TryGetProperty("versions", out var versionsProp))
            {
                if (versionsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var v in versionsProp.EnumerateArray())
                    {
                        var s = v.GetString();
                        if (!string.IsNullOrEmpty(s)) versions.Add(s);
                    }
                }
                else if (versionsProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var group in versionsProp.EnumerateObject())
                    {
                        foreach (var v in group.Value.EnumerateArray())
                        {
                            var s = v.GetString();
                            if (!string.IsNullOrEmpty(s)) versions.Add(s);
                        }
                    }
                }
            }

            versions.Sort((a, b) => CompareMinecraftVersions(b, a));
            return versions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Paper versions");
            return new List<string>();
        }
    }

    public async Task<MinecraftServer> CreateServerAsync(string name, ServerType type, string minecraftVersion, int maxRamMB, string? worldName = null, bool onlineMode = true)
    {
        _logService?.Info("ServerService", "Create", $"Creando servidor '{name}' ({type} {minecraftVersion})...");

        var safeName = SanitizeFolderName(name);
        var directoryPath = LauncherPaths.GetPath("servers", safeName);

        Directory.CreateDirectory(directoryPath);
        WriteEula(directoryPath);
        WriteServerProperties(directoryPath, worldName, onlineMode);

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

        server.Id = await _repository.CreateAsync(server).ConfigureAwait(false);

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
        await StopAsync(server).ConfigureAwait(false);

        _logService?.Info("ServerService", "Delete", $"Eliminando servidor '{server.Name}'...");

        if (Directory.Exists(server.DirectoryPath))
        {
            const int maxAttempts = 6;
            var deleted = false;
            Exception? lastError = null;

            for (var attempt = 1; attempt <= maxAttempts && !deleted; attempt++)
            {
                try
                {
                    Directory.Delete(server.DirectoryPath, true);
                    deleted = true;
                }
                catch (System.IO.IOException ex)
                {
                    lastError = ex;
                    if (attempt < maxAttempts)
                        await Task.Delay(400).ConfigureAwait(false);
                }
                catch (UnauthorizedAccessException ex)
                {
                    lastError = ex;
                    if (attempt < maxAttempts)
                        await Task.Delay(400).ConfigureAwait(false);
                }
            }

            if (!deleted)
            {
                _logService?.Warning("ServerService", "Delete",
                    $"No se pudo eliminar la carpeta del servidor porque sigue en uso: {server.DirectoryPath}. " +
                    "Cierra cualquier proceso de Java que la bloquee e intenta de nuevo.");
                throw new InvalidOperationException(
                    "No se pudo eliminar la carpeta del servidor porque está en uso por otro proceso. " +
                    $"Cierra el proceso de Java y vuelve a intentarlo. Ruta: {server.DirectoryPath}", lastError);
            }
        }

        await _repository.DeleteAsync(server.Id).ConfigureAwait(false);

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
            await KillOrphanProcessAsync(server.DirectoryPath).ConfigureAwait(false);
            Directory.CreateDirectory(server.DirectoryPath);
            if (!File.Exists(Path.Combine(server.DirectoryPath, "eula.txt")))
                WriteEula(server.DirectoryPath);

            await EnsurePauseDisabledAsync(server.DirectoryPath).ConfigureAwait(false);

            var jarPath = await EnsureServerJarAsync(server).ConfigureAwait(false);

            var javaPath = server.JavaPath;
            if (string.IsNullOrEmpty(javaPath))
            {
                _logService?.Info("Java", "ResolveStarted", "Buscando Java recomendado para el servidor...", new { server.MinecraftVersion });
                javaPath = await _javaService.GetRecommendedJavaPathAsync(server.MinecraftVersion).ConfigureAwait(false);
                if (string.IsNullOrEmpty(javaPath))
                {
                    Log($"Descargando Java necesario para el servidor...");
                javaPath = await _javaService.DownloadJavaForVersionAsync(
                    server.MinecraftVersion,
                    new Progress<double>(pct =>
                    {
                        var whole = (int)pct;
                        var msg = $"Descargando Java necesario... {whole}%";
                        ProgressChanged?.Invoke(pct, msg);
                        if (whole % 10 == 0)
                            Log($"[INFO] {msg}");
                    })).ConfigureAwait(false);
                }

                if (string.IsNullOrEmpty(javaPath))
                    return new ServerLaunchResult { Success = false, ErrorMessage = "No se pudo encontrar ni descargar Java. Revisa tu conexión." };

                server.JavaPath = javaPath;
                await _repository.UpdateAsync(server).ConfigureAwait(false);
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
                CleanupProcess(server);
                SetStatus(server, ServerStatus.Stopped);
            };

            // Registrar el proceso antes de Start para que el evento Exited (o llamadas
            // concurrentes) siempre encuentren el proceso registrado.
            lock (_lock)
            {
                _processes[server.Id] = process;
                if (!_logHistory.ContainsKey(server.Id))
                    _logHistory[server.Id] = new List<string>();
            }

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await WritePidFileAsync(server.DirectoryPath, process.Id).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write PID file for server {ServerName}", server.Name);
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
        await StopTrackedProcessAsync(server).ConfigureAwait(false);

        // Red de seguridad: un servidor iniciado en una sesion previa del launcher
        // puede seguir en ejecucion (su proceso no esta en el diccionario _processes
        // de esta sesion). Matar el huérfano referenciado por el pid file libera el
        // puerto y desbloquea la carpeta al eliminar el servidor.
        await KillOrphanProcessAsync(server.DirectoryPath).ConfigureAwait(false);
    }

    private async Task StopTrackedProcessAsync(MinecraftServer server)
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
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Server {ServerName} did not stop gracefully; killing process", server.Name);
                try { process.Kill(entireProcessTree: true); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to kill process tree for {ServerName}", server.Name); }
                try { await process.WaitForExitAsync().ConfigureAwait(false); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to wait for process exit for {ServerName}", server.Name); }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to stop server gracefully, killing process");
                try { process.Kill(entireProcessTree: true); } catch (Exception ex2) { _logger.LogWarning(ex2, "Failed to kill process tree for {ServerName}", server.Name); }
                try { await process.WaitForExitAsync().ConfigureAwait(false); } catch (Exception ex2) { _logger.LogWarning(ex2, "Failed to wait for process exit for {ServerName}", server.Name); }
            }

            CleanupProcess(server);
            SetStatus(server, ServerStatus.Stopped);
            LogServer(server.Id, $"[INFO] Servidor '{server.Name}' detenido.");
        }
    }

    public async Task StopAllAsync()
    {
        MinecraftServer[] all;
        lock (_lock)
        {
            all = _servers.ToArray();
        }

        if (all.Length == 0) return;

        _logService?.Info("ServerService", "StopAll", $"Deteniendo {all.Length} servidor(es)...");
        // Se detiene tambien a los servidores iniciados en sesiones previas (su
        // proceso no esta en _processes de esta sesion): StopAsync ahora mata el
        // huérfano referenciado por el pid file.
        var tasks = all.Select(s => Task.Run(() => StopAsync(s))).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);
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
            return _logHistory.TryGetValue(server.Id, out var lines)
                ? lines.ToList()
                : new List<string>();
        }
    }

    public async Task<string?> GetPublicIpAddressAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.ipify.org?format=text");
            request.Headers.UserAgent.ParseAdd(PaperUserAgent);
            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            var ip = (await response.Content.ReadAsStringAsync().ConfigureAwait(false)).Trim();
            return string.IsNullOrWhiteSpace(ip) ? null : ip;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve public IP address");
            return null;
        }
    }

    private async Task<string> EnsureServerJarAsync(MinecraftServer server)
    {
        var jarPath = Path.Combine(server.DirectoryPath, ServerJarName);
        if (File.Exists(jarPath)) return jarPath;

        _logService?.Info("ServerService", "DownloadJar", $"Descargando {server.Type} {server.MinecraftVersion}...");
        LogServer(server.Id, $"[INFO] Descargando jar del servidor ({server.Type} {server.MinecraftVersion})...");

        var url = server.Type == ServerType.Paper
            ? await ResolvePaperJarUrlAsync(server.MinecraftVersion).ConfigureAwait(false)
            : await _minecraftService.GetServerJarUrlAsync(server.MinecraftVersion).ConfigureAwait(false);

        if (string.IsNullOrEmpty(url))
            throw new Exception($"No se encontró el jar para {server.Type} {server.MinecraftVersion}.");

        int lastLoggedTenth = -1;
        var progress = new Progress<double>(pct =>
        {
            var whole = (int)pct;
            var msg = $"Descargando jar del servidor... {whole}%";
            ProgressChanged?.Invoke(pct, msg);
            if (whole / 10 != lastLoggedTenth || whole >= 100)
            {
                lastLoggedTenth = whole / 10;
                LogServer(server.Id, $"[INFO] {msg}");
            }
        });

        await _resumableDownloadService.DownloadAsync(url, jarPath, progress).ConfigureAwait(false);
        LogServer(server.Id, $"[INFO] Jar del servidor descargado.");
        return jarPath;
    }

    private async Task<string?> ResolvePaperJarUrlAsync(string minecraftVersion)
    {
        var buildsUrl = $"{PaperApiBaseUrl}/versions/{minecraftVersion}/builds";
        try
        {
            var doc = await GetPaperJsonAsync(buildsUrl).ConfigureAwait(false);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                return null;

            JsonElement? chosen = null;
            foreach (var build in root.EnumerateArray())
            {
                if (build.TryGetProperty("channel", out var channel) && channel.GetString() == "STABLE")
                {
                    chosen = build;
                    break;
                }
            }

            if (chosen is null)
                chosen = root[0];

            if (!chosen.Value.TryGetProperty("downloads", out var downloads)
                || !downloads.TryGetProperty("server:default", out var serverDefault))
                return null;

            if (!serverDefault.TryGetProperty("url", out var urlProp))
                return null;

            return urlProp.GetString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve Paper jar URL for {Version}", minecraftVersion);
            return null;
        }
    }

    private async Task<JsonDocument> GetPaperJsonAsync(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(PaperUserAgent);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
    }

    private static int CompareMinecraftVersions(string a, string b)
    {
        var separators = new[] { '.', '-', '_' };
        var ap = a.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        var bp = b.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        var n = Math.Max(ap.Length, bp.Length);
        for (var i = 0; i < n; i++)
        {
            var as_ = i < ap.Length ? ap[i] : "zzz";
            var bs_ = i < bp.Length ? bp[i] : "zzz";
            var ai = int.TryParse(as_, out var av);
            var bi = int.TryParse(bs_, out var bv);
            if (ai && bi)
            {
                if (av != bv) return av.CompareTo(bv);
            }
            else if (ai && !bi)
            {
                return -1;
            }
            else if (!ai && bi)
            {
                return 1;
            }
            else
            {
                var cmp = string.CompareOrdinal(as_, bs_);
                if (cmp != 0) return cmp;
            }
        }
        return 0;
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

    private void CleanupProcess(MinecraftServer server)
    {
        Process? process;
        lock (_lock)
        {
            _processes.TryGetValue(server.Id, out process);
            _processes.Remove(server.Id);
        }
        process?.Dispose();
        TryDelete(GetServerPidPath(server.DirectoryPath));
    }

    private static string GetServerPidPath(string directoryPath)
        => Path.Combine(directoryPath, ServerPidFileName);

    private async Task KillOrphanProcessAsync(string directoryPath)
    {
        var pidPath = GetServerPidPath(directoryPath);
        if (!File.Exists(pidPath)) return;

        try
        {
            var pidText = await File.ReadAllTextAsync(pidPath).ConfigureAwait(false);
            if (!int.TryParse(pidText.Trim(), out var pid) || pid <= 0)
            {
                TryDelete(pidPath);
                return;
            }

            Process? orphan = null;
            try
            {
                orphan = Process.GetProcessById(pid);
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }

            if (orphan == null)
            {
                TryDelete(pidPath);
                return;
            }

            using (orphan)
            {
                if (orphan.HasExited)
                {
                    TryDelete(pidPath);
                    return;
                }

                if (!IsJavaProcess(orphan))
                {
                    _logger.LogWarning("Pid file {PidPath} points to non-Java process {ProcessName} ({Pid}); not killing.", pidPath, orphan.ProcessName, pid);
                    TryDelete(pidPath);
                    return;
                }

                _logger.LogWarning("Killing orphan server process {ProcessName} ({Pid}) before starting.", orphan.ProcessName, pid);
                _logService?.Warning("ServerService", "OrphanKill", $"Se detectó un proceso de servidor huérfano ({orphan.ProcessName}, PID {pid}) que retenía archivos del mundo; se detendrá antes de iniciar.");
                try
                {
                    orphan.Kill(entireProcessTree: true);
                    orphan.WaitForExit(5000);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to kill orphan server process {Pid}", pid);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up orphan server process for {DirectoryPath}", directoryPath);
        }
        finally
        {
            TryDelete(pidPath);
        }
    }

    private async Task WritePidFileAsync(string directoryPath, int pid)
    {
        try
        {
            await File.WriteAllTextAsync(GetServerPidPath(directoryPath), pid.ToString()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write pid file for server at {DirectoryPath}", directoryPath);
        }
    }

    private static bool IsJavaProcess(Process process)
    {
        var name = process.ProcessName;
        return string.Equals(name, "java", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "javaw", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { }
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

    private static async Task EnsurePauseDisabledAsync(string directoryPath)
    {
        var propsPath = Path.Combine(directoryPath, "server.properties");
        if (!File.Exists(propsPath)) return;

        var content = await File.ReadAllTextAsync(propsPath).ConfigureAwait(false);
        if (content.Contains("pause-when-empty-seconds", StringComparison.OrdinalIgnoreCase))
        {
            var updated = System.Text.RegularExpressions.Regex.Replace(
                content, @"(?im)^\s*pause-when-empty-seconds\s*=.*$", "pause-when-empty-seconds=0");
            if (!string.Equals(updated, content, StringComparison.Ordinal))
                await File.WriteAllTextAsync(propsPath, updated).ConfigureAwait(false);
            return;
        }

        await File.AppendAllTextAsync(propsPath, "pause-when-empty-seconds=0\n").ConfigureAwait(false);
    }

    private static void WriteServerProperties(string directoryPath, string? worldName, bool onlineMode = true)
    {
        var levelName = SanitizeFolderName(string.IsNullOrWhiteSpace(worldName) ? "world" : worldName);
        File.WriteAllText(Path.Combine(directoryPath, "server.properties"),
            "#Minecraft server properties\n"
            + "server-port=25565\n"
            + $"level-name={levelName}\n"
            + "motd=A ShoroCraft server\n"
            + $"online-mode={onlineMode.ToString().ToLowerInvariant()}\n"
            + "max-players=20\n"
            + "view-distance=10\n"
            + "pause-when-empty-seconds=0\n");
    }

    public Task<bool> GetOnlineModeAsync(MinecraftServer server)
    {
        try
        {
            var propsPath = Path.Combine(server.DirectoryPath, "server.properties");
            if (!File.Exists(propsPath)) return Task.FromResult(true);
            foreach (var line in File.ReadLines(propsPath))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("online-mode", StringComparison.OrdinalIgnoreCase))
                {
                    var value = trimmed[(trimmed.IndexOf('=', StringComparison.Ordinal) + 1)..].Trim();
                    return Task.FromResult(!value.Equals("false", StringComparison.OrdinalIgnoreCase));
                }
            }
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(true);
        }
    }

    public Task SetOnlineModeAsync(MinecraftServer server, bool onlineMode)
    {
        try
        {
            var propsPath = Path.Combine(server.DirectoryPath, "server.properties");
            if (!File.Exists(propsPath))
            {
                WriteServerProperties(server.DirectoryPath, server.WorldName, onlineMode);
                return Task.CompletedTask;
            }

            var lines = File.ReadAllLines(propsPath);
            var replaced = false;
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim().StartsWith("online-mode", StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = $"online-mode={onlineMode.ToString().ToLowerInvariant()}";
                    replaced = true;
                    break;
                }
            }

            if (!replaced)
            {
                var list = lines.ToList();
                list.Add($"online-mode={onlineMode.ToString().ToLowerInvariant()}");
                lines = list.ToArray();
            }

            File.WriteAllLines(propsPath, lines);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update online-mode for server {Server}", server.Name);
        }

        return Task.CompletedTask;
    }

    public Task<string?> GetServerPropertiesAsync(MinecraftServer server)
    {
        try
        {
            var propsPath = Path.Combine(server.DirectoryPath, "server.properties");
            if (!File.Exists(propsPath)) return Task.FromResult<string?>(null);
            return Task.FromResult<string?>(File.ReadAllText(propsPath));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read server.properties for {Server}", server.Name);
            return Task.FromResult<string?>(null);
        }
    }

    private static readonly string[] BlockedPropertyKeys = new[]
    {
        "rcon.port", "rcon.password", "enable-rcon",
        "enable-command-block", "op-permission-level", "enable-query",
        "enforce-secure-profile"
    };

    public Task SaveServerPropertiesAsync(MinecraftServer server, string content)
    {
        try
        {
            var sanitized = SanitizeServerProperties(content);
            var propsPath = Path.Combine(server.DirectoryPath, "server.properties");
            Directory.CreateDirectory(server.DirectoryPath);
            File.WriteAllText(propsPath, sanitized);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write server.properties for {Server}", server.Name);
        }

        return Task.CompletedTask;
    }

    private string SanitizeServerProperties(string content)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;

        var lines = content.Split('\n');
        var result = new StringBuilder(content.Length + 64);
        bool first = true;

        foreach (var rawLine in lines)
        {
            if (!first) result.Append('\n');
            first = false;

            var trimmed = rawLine.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#"))
            {
                result.Append(rawLine);
                continue;
            }

            var equalsIndex = trimmed.IndexOf('=');
            if (equalsIndex <= 0)
            {
                result.Append(rawLine);
                continue;
            }

            var key = trimmed[..equalsIndex].Trim().ToLowerInvariant();
            if (BlockedPropertyKeys.Contains(key))
            {
                _logger.LogWarning("Blocked dangerous server property '{Key}' from being written", key);
                continue;
            }

            result.Append(rawLine);
        }

        return result.ToString();
    }

    private static string SanitizeFolderName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "server" : name;
    }
}
