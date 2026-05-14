using System.Diagnostics;
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
    private Process? _gameProcess;

    public event Action<string>? LogOutput;
    public event Action<double, string>? ProgressChanged;
    public event Action? GameExited;
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

    public async Task<LaunchResult> LaunchProfileAsync(Profile profile, AuthResult auth)
    {
        using var operation = _logService?.BeginOperation("Launch", "LaunchProfile", new { profile.Name, profile.MinecraftVersion, profile.Type });

        if (IsGameRunning)
        {
            _logger.LogWarning("A game is already running");
            _logService?.Warning("Launch", "AlreadyRunning", "Ya hay una partida en ejecución.");
            return new LaunchResult { Success = false, ErrorMessage = "Ya hay una partida en ejecución." };
        }

        try
        {
            _logger.LogInformation("Launching profile {ProfileName}", profile.Name);
            _logService?.Info("Launch", "PreflightStarted", "Validando perfil antes de iniciar.", new { profile.Name, profile.MinecraftVersion, profile.Type });

            var gameDir = string.IsNullOrEmpty(profile.GameDirectory)
                ? _minecraftService.GetDefaultGameDirectory(profile.Name)
                : profile.GameDirectory;

            Directory.CreateDirectory(gameDir);
            _logService?.Debug("Launch", "GameDirectoryReady", "Directorio de juego listo.", new { gameDir });
            _logger.LogInformation("Repairing installation in {GameDir}", gameDir);
            await _minecraftService.RepairInstallationAsync(gameDir);
            _logger.LogInformation("Installation repair completed");

            var javaPath = profile.JavaPath;
            if (string.IsNullOrEmpty(javaPath))
            {
                _logger.LogInformation("Java path not specified, trying to find recommended Java");
                _logService?.Info("Java", "ResolveStarted", "Buscando Java recomendado.", new { profile.MinecraftVersion });
                var recommended = await _javaService.GetRecommendedJavaPathAsync(profile.MinecraftVersion);
                if (string.IsNullOrEmpty(recommended))
                {
                    _logger.LogInformation("Recommended Java not found, starting auto-download");
                    Log("[INFO] Descargando Java necesario para esta versión...");
                    recommended = await _javaService.DownloadJavaForVersionAsync(profile.MinecraftVersion);
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

            var process = await _minecraftService.LaunchGameAsync(
                profile, gameDir, javaPath,
                auth.AccessToken ?? "offline",
                auth.Uuid ?? Guid.NewGuid().ToString(),
                auth.Username ?? "Player",
                (pct, msg) => {
                    Log($"[INFO] {msg}");
                    ProgressChanged?.Invoke(pct, msg);
                });

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    _logger.LogDebug("Minecraft: {Line}", e.Data);
                    _logService?.MinecraftStdout(e.Data);
                    Log(e.Data);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    _logger.LogError("Minecraft: {Line}", e.Data);
                    _logService?.MinecraftStderr(e.Data);
                    Log($"[ERROR] {e.Data}");
                }
            };

            process.EnableRaisingEvents = true;
            process.Exited += (s, _) =>
            {
                var p = (Process)s!;
                _logger.LogInformation("Minecraft process exited with code {ExitCode}", p.ExitCode);
                _logService?.Info("Launch", "ProcessExited", "Proceso de Minecraft terminado.", new { p.ExitCode });
                Log($"Proceso terminado con código {p.ExitCode}");
                _gameProcess = null;
                GameExited?.Invoke();
            };

            _logger.LogDebug("Starting process: {FileName} {Arguments}", process.StartInfo.FileName, process.StartInfo.Arguments);
            _logService?.Info("Launch", "ProcessStarting", "Iniciando proceso de Minecraft.", new { process.StartInfo.FileName, process.StartInfo.Arguments });
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _gameProcess = process;

            _logger.LogInformation("Game launched with PID {ProcessId}", process.Id);
            _logService?.Info("Launch", "ProcessStarted", "Minecraft iniciado.", new { process.Id });
            return new LaunchResult
            {
                Success = true,
                ProcessId = process.Id
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch game");
            _logService?.Error("Launch", "Failed", "Error al iniciar Minecraft.", ex);
            return new LaunchResult
            {
                Success = false,
                ErrorMessage = $"Error al iniciar: {ex.Message}"
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

    public async Task StopGameAsync()
    {
        if (_gameProcess is { HasExited: false })
        {
            _logger.LogInformation("Stopping game process");
            _logService?.Warning("Launch", "StopRequested", "Deteniendo Minecraft por solicitud del usuario.");
            _gameProcess.Kill(entireProcessTree: true);
            _gameProcess.WaitForExit(5000);
            _gameProcess = null;
            Log("Juego detenido por el usuario.");
        }
    }
}
