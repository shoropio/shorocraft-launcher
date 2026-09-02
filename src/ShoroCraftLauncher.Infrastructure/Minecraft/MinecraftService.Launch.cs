using System.Diagnostics;
using System.IO.Compression;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Infrastructure.Downloading;

namespace ShoroCraftLauncher.Infrastructure.Minecraft;


public partial class MinecraftService : IMinecraftService
{
    #region Lanzamiento del juego

    public async Task<IGameProcess> LaunchGameAsync(Profile profile, string gameDir, string javaPath, string accessToken, string uuid, string username, Action<double, string>? onProgress = null)
    {
        _logger.LogInformation("Launching: profile={Profile}, version={Version}", profile.Name, profile.MinecraftVersion);

        var launchStartTime = DateTime.UtcNow;
        var globalDir = gameDir;
        var targetVersion = profile.MinecraftVersion;

        if (profile.MinecraftVersion.ToLower() == "latest")
        {
            targetVersion = await ResolveVersionIdAsync("latest").ConfigureAwait(false);
        }

        if (profile.Type != Core.Enums.ProfileType.Vanilla)
        {
            var loaderPrefix = profile.Type.ToString().ToLower();
            var versionsDir = Path.Combine(globalDir, "versions");
            if (Directory.Exists(versionsDir))
            {
                // Coincide solo con la carpeta del loader cuya versión de Minecraft sea exacta
                // (p.ej. fabric-loader-0.16.0-1.21 para 1.21, no 1.21.1).
                var match = Directory.GetDirectories(versionsDir)
                    .Select(Path.GetFileName)
                    .FirstOrDefault(n => n != null
                        && n.Contains(loaderPrefix, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(
                            n.Split('-', StringSplitOptions.RemoveEmptyEntries).LastOrDefault(),
                            targetVersion,
                            StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    targetVersion = match;
                }
            }
        }

        onProgress?.Invoke(-1, $"Preparando lanzamiento de {targetVersion}...");

        var session = accessToken.Equals("offline", StringComparison.OrdinalIgnoreCase)
            ? CmlLib.Core.Auth.MSession.CreateOfflineSession(username)
            : new CmlLib.Core.Auth.MSession(username, accessToken, uuid)
            {
                UserType = "msa"
            };

        var launchOption = new CmlLib.Core.ProcessBuilder.MLaunchOption
        {
            MaximumRamMb = profile.MaxRamMB,
            MinimumRamMb = profile.MinRamMB,
            Session = session,
            JavaPath = javaPath,
            VersionType = "ShoroCraft Launcher",
            GameLauncherName = "ShoroCraft",
            GameLauncherVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0",
            ScreenWidth = profile.WindowWidth,
            ScreenHeight = profile.WindowHeight,
            FullScreen = profile.IsFullscreen
        };

        var process = await InstallAndBuildWithRetryAsync(globalDir, targetVersion, launchOption, onProgress).ConfigureAwait(false);
        
        process.StartInfo.WorkingDirectory = gameDir;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.Arguments = EnsureNativeAccessModules(process.StartInfo.Arguments);

        _logger.LogInformation("Launch duration: {Duration}s", (DateTime.UtcNow - launchStartTime).TotalSeconds);
        return new GameProcess(process);
    }

    #endregion

    #region Reintentos de instalación y cliente HTTP

    private async Task<Process> InstallAndBuildWithRetryAsync(
        string globalDir,
        string targetVersion,
        CmlLib.Core.ProcessBuilder.MLaunchOption launchOption,
        Action<double, string>? onProgress)
    {
        var mcPath = new CmlLib.Core.MinecraftPath(globalDir);
        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxInstallAttempts; attempt++)
        {
            using var cts = new CancellationTokenSource(InstallAttemptTimeout);
            using var cmlClient = CreateCmlHttpClient();

            var parameters = CmlLib.Core.MinecraftLauncherParameters.CreateDefault(mcPath, cmlClient);
            var launcher = new CmlLib.Core.MinecraftLauncher(parameters);

            int lastReportedPercent = -1;
            launcher.FileProgressChanged += (s, e) =>
            {
                var percentage = e.TotalTasks > 0 ? (double)e.ProgressedTasks / e.TotalTasks * 100 : 0;
                var percent = (int)Math.Floor(percentage);
                var shouldReport = lastReportedPercent < 0
                    || percent >= 100
                    || percent - lastReportedPercent >= 5;

                if (shouldReport)
                {
                    lastReportedPercent = percent;
                    onProgress?.Invoke(percentage, $"Verificando archivos de Minecraft... {percent}% ({e.ProgressedTasks}/{e.TotalTasks})");
                }
            };

            try
            {
                return await launcher
                    .InstallAndBuildProcessAsync(targetVersion, launchOption, cancellationToken: cts.Token)
                    .AsTask().ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < MaxInstallAttempts && IsRetryableInstallError(ex))
            {
                lastError = ex;
                _logger.LogWarning(ex, "Instalación de Minecraft falló en el intento {Attempt} ({Type}); reintentando.", attempt, ex.GetType().Name);
                _logService?.Warning("Launch", "InstallRetry", "Error de red al descargar Minecraft. Reintentando.", new { attempt, maxAttempts = MaxInstallAttempts });
                onProgress?.Invoke(-1, $"Error de red al descargar Minecraft. Reintentando (intento {attempt}/{MaxInstallAttempts})...");
            }
        }

        throw lastError ?? new InvalidOperationException("No se pudo instalar Minecraft.");
    }

    private static HttpClient CreateCmlHttpClient()
    {
        var handler = new RetryDelegatingHandler
        {
            InnerHandler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            }
        };

        return new HttpClient(handler)
        {
            Timeout = CmlHttpTimeout
        };
    }

    private static bool IsRetryableInstallError(Exception ex)
        => ex is OperationCanceledException
           || ex is HttpRequestException
           || ex is IOException
           || ex is SocketException
           || ex is TimeoutException;

    private static string EnsureNativeAccessModules(string arguments)
    {
        const string current = "--enable-native-access=ALL-UNNAMED";
        const string expanded = "--enable-native-access=ALL-UNNAMED,org.lwjgl,org.lwjgl.opengl,org.lwjgl.stb,com.sun.jna";

        if (arguments.Contains(expanded, StringComparison.OrdinalIgnoreCase))
            return arguments;

        if (arguments.Contains(current, StringComparison.OrdinalIgnoreCase))
            return arguments.Replace(current, expanded, StringComparison.OrdinalIgnoreCase);

        return $"{expanded} {arguments}";
    }

    #endregion

}
