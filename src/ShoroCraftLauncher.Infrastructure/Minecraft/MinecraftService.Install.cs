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
    #region Instalación de versiones y loaders

    public async Task InstallVersionAsync(string versionId, IProgress<double>? progress = null, string? gameDir = null)
    {
        gameDir = ResolveGameDirectory(gameDir);
        using var operation = _logService?.BeginOperation("MinecraftInstall", "InstallVersion", new { versionId, gameDir });
        _logger.LogInformation("Installing Minecraft version {Version}", versionId);
        _logService?.Info("MinecraftInstall", "Started", "Instalando versión de Minecraft.", new { versionId });
        using var versionData = await FetchVersionDataAsync(versionId).ConfigureAwait(false);
        if (versionData == null)
            throw new Exception($"Version {versionId} not found");

        var versionsDir = Path.Combine(gameDir, "versions", versionId);
        var installMarker = Path.Combine(versionsDir, ".shorocraft-installed.json");
        Directory.CreateDirectory(Path.Combine(gameDir, "versions"));
        if (IsVersionComplete(versionsDir, versionId))
        {
            _logService?.Info("MinecraftInstall", "AlreadyComplete", "La versión ya está instalada.", new { versionId, versionsDir });
            await EnsureLauncherProfileAsync(gameDir, versionId).ConfigureAwait(false);
            return;
        }

        var tempRoot = Path.Combine(gameDir, "versions", ".installing");
        var tempVersionsDir = Path.Combine(tempRoot, $"{versionId}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempVersionsDir);

        try
        {
            var jarPath = Path.Combine(tempVersionsDir, $"{versionId}.jar");
            var clientUrl = versionData.GetClientUrl();
            if (clientUrl == null) throw new Exception($"No download URL for version {versionId}");
            _logger.LogInformation("Downloading client jar for {Version}", versionId);
            _logService?.Info("MinecraftInstall", "ClientDownloadStarted", "Descargando cliente de Minecraft.", new { versionId, clientUrl });
            await DownloadFileAsync(clientUrl, jarPath, progress).ConfigureAwait(false);

            var jsonPath = Path.Combine(tempVersionsDir, $"{versionId}.json");
            var versionJson = await _httpClient.GetStringAsync(versionData.Url).ConfigureAwait(false);
            await File.WriteAllTextAsync(jsonPath, versionJson).ConfigureAwait(false);

            var libsDir = Path.Combine(gameDir, "libraries");
            var libCount = await DownloadLibrariesAsync(versionData, libsDir, progress).ConfigureAwait(false);

            if (Directory.Exists(versionsDir))
                Directory.Delete(versionsDir, recursive: true);
            Directory.Move(tempVersionsDir, versionsDir);

            await File.WriteAllTextAsync(installMarker, JsonSerializer.Serialize(new
            {
                versionId,
                installedAt = DateTimeOffset.Now,
                launcher = "ShoroCraftLauncher"
            }, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);

            await EnsureLauncherProfileAsync(gameDir, versionId).ConfigureAwait(false);

            _logger.LogInformation("Version {Version} installed ({LibCount} libraries)", versionId, libCount);
            _logService?.Info("MinecraftInstall", "Completed", "Versión de Minecraft instalada correctamente.", new { versionId, libCount });
        }
        catch (Exception ex)
        {
            _logService?.Error("MinecraftInstall", "Failed", "Falló la instalación de Minecraft.", ex, new { versionId });
            throw;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempVersionsDir))
                    Directory.Delete(tempVersionsDir, recursive: true);
            }
            catch (Exception ex)
            {
                _logService?.Warning("MinecraftInstall", "TempCleanupFailed", "No se pudo limpiar carpeta temporal.", new { tempVersionsDir, ex.Message });
            }
        }
    }

    public async Task InstallLoaderAsync(string versionId, string loaderType, string loaderVersion, string javaPath, Action<string>? onProgress = null, IProgress<double>? progress = null, Action<string>? onLog = null, string? gameDir = null)
    {
        gameDir = ResolveGameDirectory(gameDir);
        using var operation = _logService?.BeginOperation("LoaderInstall", "InstallLoader", new { versionId, loaderType, loaderVersion, gameDir });
        _logger.LogInformation("Installing loader {Loader} for Minecraft {McVersion}", loaderType, versionId);
        _logService?.Info("LoaderInstall", "Started", "Instalando loader.", new { versionId, loaderType, loaderVersion });
        onLog?.Invoke($"[INFO] Preparando instalación de {loaderType} {loaderVersion}...");
        onProgress?.Invoke($"Preparando instalación de {loaderType} {loaderVersion}...");
        
        Directory.CreateDirectory(Path.Combine(gameDir, "cache"));

        var versionDir = Path.Combine(gameDir, "versions", versionId);
        if (!Directory.Exists(versionDir) || !File.Exists(Path.Combine(versionDir, $"{versionId}.jar")))
        {
            _logService?.Warning("LoaderInstall", "BaseVersionMissing", "Minecraft base no está instalado; se instalará antes del loader.", new { versionId });
            onLog?.Invoke($"[INFO] Minecraft {versionId} no está instalado. Instalando versión base...");
            onProgress?.Invoke($"Instalando Minecraft {versionId}...");
            await InstallVersionAsync(versionId, progress, gameDir).ConfigureAwait(false);
        }

        await EnsureLauncherProfileAsync(gameDir, versionId).ConfigureAwait(false);

        var (installerVersion, installerUrl, installerPath) = await ResolveLoaderInstallerInfoAsync(versionId, loaderType, loaderVersion, gameDir).ConfigureAwait(false);

        if (!File.Exists(installerPath))
        {
            _logService?.Info("LoaderInstall", "InstallerDownloadStarted", "Descargando instalador de loader.", new { loaderType, installerUrl });
            onLog?.Invoke($"[INFO] Descargando instalador de {loaderType}...");
            onProgress?.Invoke($"Descargando instalador de {loaderType}...");
            try
            {
                await DownloadFileAsync(installerUrl, installerPath, progress).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Failed to download {Loader} installer from {Url}", loaderType, installerUrl);
                _logService?.Error("LoaderInstall", "InstallerDownloadFailed", "No se pudo descargar el instalador del loader.", ex, new { loaderType, installerUrl });
                throw new Exception($"No se pudo descargar el instalador de {loaderType} ({(int)(ex.StatusCode ?? 0)}). Verifica que la versión sea correcta: {installerUrl}");
            }
        }
        
        _logger.LogInformation("Loader installer downloaded to {Path}. Starting installation.", installerPath);
        onLog?.Invoke($"[INFO] Ejecutando instalador de {loaderType}...");
        onProgress?.Invoke($"Ejecutando instalador de {loaderType}...");

        // Log Java version
        try
        {
            var javaVersionPsi = new ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var jp = Process.Start(javaVersionPsi);
            if (jp != null)
            {
                var javaVer = await jp.StandardError.ReadToEndAsync().ConfigureAwait(false);
                _logger.LogInformation("Java version: {JavaVersion}", javaVer.Trim());
                onLog?.Invoke($"[INFO] Java: {javaVer.Trim()}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not check Java version");
        }

        var args = loaderType.ToLower() switch
        {
            "forge" => $"-jar \"{installerPath}\" --installClient \"{gameDir}\"",
            "neoforge" => $"-jar \"{installerPath}\" --installClient \"{gameDir}\"",
            "fabric" => $"-jar \"{installerPath}\" client -dir \"{gameDir}\" -mcversion {versionId} -loader {loaderVersion}",
            "quilt" => $"-jar \"{installerPath}\" install client {versionId} --install-dir=\"{gameDir}\"",
            _ => throw new Exception($"Unknown loader: {loaderType}")
        };

        _logger.LogInformation("Java: {Java} | Args: {Args}", javaPath, args);
        _logService?.Info("LoaderInstall", "InstallerStarting", "Ejecutando instalador del loader.", new { javaPath, args });

        var psi = new ProcessStartInfo
        {
            FileName = javaPath.Replace("javaw.exe", "java.exe"),
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var outputLines = new List<string>();
        var errorLines = new List<string>();
        var lineLock = new object();

        using var process = new Process { StartInfo = psi };
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (lineLock) AddBoundedLine(outputLines, e.Data);
            if (ShouldEchoLoaderInstallerLine(e.Data))
                onLog?.Invoke($"[{loaderType}] {e.Data}");
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (lineLock) AddBoundedLine(errorLines, e.Data);
            onLog?.Invoke($"[ERROR] [{loaderType}] {e.Data}");
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            try { await process.WaitForExitAsync().ConfigureAwait(false); } catch { }
            throw new Exception($"El instalador de {loaderType} tardó más de 5 minutos y fue cancelado.");
        }

        // Esperar a que terminen los eventos de salida asíncronos antes de leer el buffer
        try { process.WaitForExit(); } catch { }

        if (process.ExitCode != 0)
        {
            List<string> outputSnapshot;
            List<string> errorSnapshot;
            lock (lineLock)
            {
                outputSnapshot = new List<string>(outputLines);
                errorSnapshot = new List<string>(errorLines);
            }
            var allOutput = string.Join(Environment.NewLine, outputSnapshot.Concat(errorSnapshot));
            var detail = !string.IsNullOrEmpty(allOutput) ? $": {allOutput}" : "";
            _logger.LogError("Installer failed. Exit code: {ExitCode}. Full output: {Output}", process.ExitCode, allOutput);
            _logService?.Error("LoaderInstall", "InstallerFailed", "El instalador del loader falló.", data: new { loaderType, process.ExitCode, output = allOutput });
            throw new Exception($"El instalador de {loaderType} falló con código {process.ExitCode}{detail}");
        }

        _logger.LogInformation("Loader {Loader} installed successfully.", loaderType);
        _logService?.Info("LoaderInstall", "Completed", "Loader instalado correctamente.", new { loaderType, loaderVersion, versionId });
        onLog?.Invoke($"[INFO] {loaderType} instalado correctamente.");
        onProgress?.Invoke($"{loaderType} instalado correctamente.");
    }

    private static void AddBoundedLine(List<string> lines, string line, int maxLines = 500)
    {
        lines.Add(line);
        if (lines.Count > maxLines)
            lines.RemoveRange(0, lines.Count - maxLines);
    }

    private static bool ShouldEchoLoaderInstallerLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var text = line.Trim();
        if (text.StartsWith("Considering ", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Copying ", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Reading patch ", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Applying: ", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("  "))
        {
            return false;
        }

        return text.Contains("exception", StringComparison.OrdinalIgnoreCase)
            || text.Contains("error", StringComparison.OrdinalIgnoreCase)
            || text.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || text.Contains("complete", StringComparison.OrdinalIgnoreCase)
            || text.Contains("success", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("JVM info", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Target Directory", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Installing", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Building", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(string installerVersion, string installerUrl, string installerPath)> ResolveLoaderInstallerInfoAsync(
        string versionId, string loaderType, string loaderVersion, string gameDir)
    {
        gameDir = ResolveGameDirectory(gameDir);
        var installerVersion = loaderType.Equals("fabric", StringComparison.OrdinalIgnoreCase)
            ? await ResolveLatestFabricInstallerVersionAsync().ConfigureAwait(false)
            : loaderVersion;

        var installerUrl = loaderType.ToLower() switch
        {
            "forge" => $"https://maven.minecraftforge.net/net/minecraftforge/forge/{versionId}-{loaderVersion}/forge-{versionId}-{loaderVersion}-installer.jar",
            "neoforge" => $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{versionId}-{loaderVersion}/neoforge-{versionId}-{loaderVersion}-installer.jar",
            "fabric" => $"https://maven.fabricmc.net/net/fabricmc/fabric-installer/{installerVersion}/fabric-installer-{installerVersion}.jar",
            "quilt" => $"https://maven.quiltmc.net/release/org/quiltmc/quilt-installer/{loaderVersion}/quilt-installer-{loaderVersion}.jar",
            _ => throw new Exception($"Unknown loader: {loaderType}")
        };

        var installerPath = Path.Combine(gameDir, "cache", $"{loaderType}-installer-{versionId}-{installerVersion}.jar");
        return (installerVersion, installerUrl, installerPath);
    }

    public async Task PreDownloadLoaderInstallerAsync(string versionId, string loaderType, string loaderVersion, string gameDir, IProgress<double>? progress = null)
    {
        var (_, installerUrl, installerPath) = await ResolveLoaderInstallerInfoAsync(versionId, loaderType, loaderVersion, gameDir).ConfigureAwait(false);
        if (!File.Exists(installerPath))
            await DownloadFileAsync(installerUrl, installerPath, progress).ConfigureAwait(false);
    }

    #endregion

}
