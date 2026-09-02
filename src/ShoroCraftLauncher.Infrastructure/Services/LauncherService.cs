using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Infrastructure.Services;

public class LauncherService : ILauncherService
{
    private readonly IMinecraftService _minecraftService;
    private readonly IJavaService _javaService;
    private readonly IAuthenticationService _authenticationService;
    private readonly ILogger<LauncherService> _logger;
    private readonly ILogService? _logService;
    private readonly List<string> _logHistory = new();
    private readonly object _logLock = new();
    private IGameProcess? _gameProcess;

    public event Action<string>? LogOutput;
    public event Action<double, string>? ProgressChanged;
    public event Action? ProgressCompleted;
    public event Action<int>? GameExited;
    public bool IsGameRunning => _gameProcess is { HasExited: false };
    public IReadOnlyList<string> LogHistory
    {
        get
        {
            lock (_logLock)
                return _logHistory.ToList();
        }
    }

    public LauncherService(
        IMinecraftService minecraftService,
        IJavaService javaService,
        IAuthenticationService authenticationService,
        ILogger<LauncherService> logger,
        ILogService? logService = null)
    {
        _minecraftService = minecraftService;
        _javaService = javaService;
        _authenticationService = authenticationService;
        _logger = logger;
        _logService = logService;
    }

    public Task<LaunchResult> LaunchProfileAsync(Profile profile, AuthResult auth)
    {
        return LaunchProfileInternalAsync(profile, auth, false);
    }

    private async Task<LaunchResult> LaunchProfileInternalAsync(Profile profile, AuthResult auth, bool isRetry)
    {
        using var operation = _logService?.BeginOperation("Launch", "LaunchProfile", new { profile.Name, profile.MinecraftVersion, profile.Type });

        if (IsGameRunning)
        {
            _logger.LogWarning("A game is already running");
            _logService?.Warning("Launch", "AlreadyRunning", "Ya hay una partida en ejecución.");
            return new LaunchResult { Success = false, ErrorMessage = "Ya hay una partida en ejecución." };
        }

        var gameDir = string.IsNullOrEmpty(profile.GameDirectory)
            ? _minecraftService.GetDefaultGameDirectory(profile.Name)
            : profile.GameDirectory;

        try
        {
            _logger.LogInformation("Launching profile {ProfileName}", profile.Name);
            _logService?.Info("Launch", "PreflightStarted", "Validando perfil antes de iniciar.", new { profile.Name, profile.MinecraftVersion, profile.Type });
            Log($"[INFO] Iniciando perfil '{profile.Name}' (Minecraft {profile.MinecraftVersion}, {profile.Type})...");
            ProgressChanged?.Invoke(-1, "Validando perfil...");

            Directory.CreateDirectory(gameDir);
            _logService?.Debug("Launch", "GameDirectoryReady", "Directorio de juego listo.", new { gameDir });
            _logger.LogInformation("Repairing installation in {GameDir}", gameDir);
            Log($"[INFO] Verificando la instalación en {gameDir}...");
            await _minecraftService.RepairInstallationAsync(gameDir).ConfigureAwait(false);
            _logger.LogInformation("Installation repair completed");
            Log("[INFO] Verificación de instalación completada.");

            var javaPath = profile.JavaPath;
            if (string.IsNullOrEmpty(javaPath))
            {
                _logger.LogInformation("Java path not specified, trying to find recommended Java");
                _logService?.Info("Java", "ResolveStarted", "Buscando Java recomendado.", new { profile.MinecraftVersion });
                var recommended = await _javaService.GetRecommendedJavaPathAsync(profile.MinecraftVersion).ConfigureAwait(false);
                if (string.IsNullOrEmpty(recommended))
                {
                    _logger.LogInformation("Recommended Java not found, starting auto-download");
                    Log("[INFO] Descargando Java necesario para esta versión...");
                    recommended = await _javaService.DownloadJavaForVersionAsync(
                        profile.MinecraftVersion,
                        new Progress<double>(pct =>
                        {
                            var msg = $"Descargando Java necesario... {pct:0}%";
                            Log($"[INFO] {msg}");
                            ProgressChanged?.Invoke(pct, msg);
                        })).ConfigureAwait(false);
                }
                
                if (string.IsNullOrEmpty(recommended))
                {
                    _logService?.Error("Java", "ResolveFailed", "No se pudo encontrar ni descargar Java.");
                    return new LaunchResult { Success = false, ErrorMessage = "No se pudo encontrar ni descargar Java. Revisa tu conexión." };
                }
                
                javaPath = recommended;
            }
            _logger.LogInformation("Using Java path: {JavaPath}", javaPath);
            _logService?.Info("Java", "Selected", "Java seleccionado.", new { javaPath });
            Log($"[INFO] Java seleccionado: {javaPath}");
            Log("[INFO] Preparando archivos de Minecraft (descargando lo que falte)...");
            ProgressChanged?.Invoke(-1, "Preparando lanzamiento...");

            var process = await _minecraftService.LaunchGameAsync(
                profile, gameDir, javaPath,
                auth.AccessToken ?? "offline",
                auth.Uuid ?? Guid.NewGuid().ToString(),
                auth.Username ?? "Player",
                (pct, msg) => {
                    Log($"[INFO] {msg}");
                    ProgressChanged?.Invoke(pct, msg);
                }).ConfigureAwait(false);

            process.OutputLineReceived += line =>
            {
                if (line != null)
                {
                    _logger.LogDebug("Minecraft: {Line}", line);
                    _logService?.MinecraftStdout(line);
                }
            };

            process.ErrorLineReceived += line =>
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    if (IsMinecraftStderrWarning(line))
                    {
                        _logger.LogWarning("Minecraft: {Line}", line);
                        _logService?.Warning("Minecraft", "StderrWarning", line);
                        Log($"[WARN] {line}");
                    }
                    else
                    {
                        _logger.LogError("Minecraft: {Line}", line);
                        _logService?.MinecraftStderr(line);
                        Log($"[ERROR] {line}");
                    }
                }
            };

            process.Exited += exitCode =>
            {
                _logger.LogInformation("Minecraft process exited with code {ExitCode}", exitCode);
                _logService?.Info("Launch", "ProcessExited", "Proceso de Minecraft terminado.", new { ExitCode = exitCode });
                Log($"Proceso terminado con código {exitCode}");
                _gameProcess = null;
                GameExited?.Invoke(exitCode);
            };

            var sanitizedArgs = LogService.Sanitize(process.Arguments);
            _logger.LogDebug("Starting process: {FileName} {Arguments}", process.FileName, sanitizedArgs);
            _logService?.Info("Launch", "ProcessStarting", "Iniciando proceso de Minecraft.", new { process.FileName, Arguments = sanitizedArgs });
            Log("[INFO] Archivos listos. Iniciando Minecraft...");
            ProgressChanged?.Invoke(100, "Iniciando Minecraft...");
            process.Start();

            _gameProcess = process;

            _logger.LogInformation("Game launched with PID {ProcessId}", process.Id);
            _logService?.Info("Launch", "ProcessStarted", "Minecraft iniciado.", new { process.Id });
            ProgressCompleted?.Invoke();
            return new LaunchResult
            {
                Success = true,
                ProcessId = process.Id
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch game");
            var errorMsg = ex.Message;
            var shouldRepair = ex is DirectoryNotFoundException
                || errorMsg.Contains("Could not find path", StringComparison.OrdinalIgnoreCase)
                || errorMsg.Contains("Could not find a part of the path", StringComparison.OrdinalIgnoreCase);

            if (shouldRepair && !isRetry)
            {
                _logger.LogWarning(ex, "Path issue detected, attempting repair and retry.");
                _logService?.Warning("Launch", "PathRepair", "Se detectó un error de ruta; intentando reparar e iniciar de nuevo.");
                try
                {
                    await _minecraftService.RepairInstallationAsync(gameDir).ConfigureAwait(false);
                    return await LaunchProfileInternalAsync(profile, auth, true).ConfigureAwait(false);
                }
                catch (Exception retryEx)
                {
                    _logger.LogError(retryEx, "Retry after repair failed");
                }

                errorMsg = "La carpeta del perfil no existía o estaba corrupta; fue recreada. Por favor, intenta de nuevo.";
            }

            _logService?.Error("Launch", "Failed", "Error al iniciar Minecraft.", ex);
            return new LaunchResult
            {
                Success = false,
                ErrorMessage = errorMsg
            };
        }
    }

    public void Log(string message)
    {
        lock (_logLock)
        {
            _logHistory.Add(message);
            if (_logHistory.Count > 2000)
                _logHistory.RemoveRange(0, _logHistory.Count - 2000);
        }

        LogOutput?.Invoke(message);
        _logService?.Info("Console", "Message", message);
    }

    private static bool IsMinecraftStderrWarning(string line)
    {
        return line.TrimStart().StartsWith("WARNING:", StringComparison.OrdinalIgnoreCase);
    }

    public async Task StopGameAsync()
    {
        // Capturar el proceso primero: el manejador Exited puede anular _gameProcess
        // concurrentemente (race que causaba NullReferenceException).
        var proc = _gameProcess;
        if (proc is null) return;

        _gameProcess = null;

        if (!proc.HasExited)
        {
            _logger.LogInformation("Stopping game process");
            _logService?.Warning("Launch", "StopRequested", "Deteniendo Minecraft por solicitud del usuario.");
            try { proc.Kill(); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to kill game process"); }
            try { await proc.WaitForExitAsync().ConfigureAwait(false); } catch { }
            Log("Juego detenido por el usuario.");
        }

        proc.Dispose();
    }
}
