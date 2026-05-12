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
    private Process? _gameProcess;

    public event Action<string>? LogOutput;
    public event Action<double, string>? ProgressChanged;
    public event Action? GameExited;
    public bool IsGameRunning => _gameProcess is { HasExited: false };

    public LauncherService(
        IMinecraftService minecraftService,
        IJavaService javaService,
        IAuthenticationService authenticationService,
        ILogger<LauncherService> logger)
    {
        _minecraftService = minecraftService;
        _javaService = javaService;
        _authenticationService = authenticationService;
        _logger = logger;
    }

    public async Task<LaunchResult> LaunchProfileAsync(Profile profile, AuthResult auth)
    {
        if (IsGameRunning)
        {
            _logger.LogWarning("A game is already running");
            return new LaunchResult { Success = false, ErrorMessage = "Ya hay una partida en ejecución." };
        }

        try
        {
            _logger.LogInformation("Launching profile {ProfileName}", profile.Name);

            var gameDir = string.IsNullOrEmpty(profile.GameDirectory)
                ? _minecraftService.GetDefaultGameDirectory(profile.Name)
                : profile.GameDirectory;

            Directory.CreateDirectory(gameDir);
            _logger.LogInformation("Repairing installation in {GameDir}", gameDir);
            await _minecraftService.RepairInstallationAsync(gameDir);
            _logger.LogInformation("Installation repair completed");

            var javaPath = profile.JavaPath;
            if (string.IsNullOrEmpty(javaPath))
            {
                _logger.LogInformation("Java path not specified, trying to find recommended Java");
                var recommended = await _javaService.GetRecommendedJavaPathAsync(profile.MinecraftVersion);
                if (string.IsNullOrEmpty(recommended))
                {
                    _logger.LogInformation("Recommended Java not found, starting auto-download");
                    LogOutput?.Invoke("[INFO] Descargando Java necesario para esta versión...");
                    recommended = await _javaService.DownloadJavaForVersionAsync(profile.MinecraftVersion);
                }
                
                if (string.IsNullOrEmpty(recommended))
                    return new LaunchResult { Success = false, ErrorMessage = "No se pudo encontrar ni descargar Java. Revisa tu conexión." };
                
                javaPath = recommended;
            }
            _logger.LogInformation("Using Java path: {JavaPath}", javaPath);

            var process = await _minecraftService.LaunchGameAsync(
                profile, gameDir, javaPath,
                auth.AccessToken ?? "offline",
                auth.Uuid ?? Guid.NewGuid().ToString(),
                auth.Username ?? "Player",
                (pct, msg) => {
                    LogOutput?.Invoke($"[INFO] {msg}");
                    ProgressChanged?.Invoke(pct, msg);
                });

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    _logger.LogDebug("Minecraft: {Line}", e.Data);
                    LogOutput?.Invoke(e.Data);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    _logger.LogError("Minecraft: {Line}", e.Data);
                    LogOutput?.Invoke($"[ERROR] {e.Data}");
                }
            };

            process.EnableRaisingEvents = true;
            process.Exited += (s, _) =>
            {
                var p = (Process)s!;
                _logger.LogInformation("Minecraft process exited with code {ExitCode}", p.ExitCode);
                LogOutput?.Invoke($"Proceso terminado con código {p.ExitCode}");
                _gameProcess = null;
                GameExited?.Invoke();
            };

            _logger.LogDebug("Starting process: {FileName} {Arguments}", process.StartInfo.FileName, process.StartInfo.Arguments);
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            _gameProcess = process;

            _logger.LogInformation("Game launched with PID {ProcessId}", process.Id);
            return new LaunchResult
            {
                Success = true,
                ProcessId = process.Id
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch game");
            return new LaunchResult
            {
                Success = false,
                ErrorMessage = $"Error al iniciar: {ex.Message}"
            };
        }
    }

    public async Task StopGameAsync()
    {
        if (_gameProcess is { HasExited: false })
        {
            _logger.LogInformation("Stopping game process");
            _gameProcess.Kill(entireProcessTree: true);
            _gameProcess.WaitForExit(5000);
            _gameProcess = null;
            LogOutput?.Invoke("Juego detenido por el usuario.");
        }
    }
}
