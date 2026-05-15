using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Infrastructure.Minecraft;

public class MinecraftService : IMinecraftService
{
    private readonly ILogger<MinecraftService> _logger;
    private readonly HttpClient _httpClient;
    private readonly ILogService? _logService;
    private const string VersionManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private const string ForgePromotionsUrl = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
    private const string FabricGameVersionsUrl = "https://meta.fabricmc.net/v2/versions/game";
    private const string FabricLoaderVersionsUrl = "https://meta.fabricmc.net/v2/versions/loader";
    private const string FabricInstallerVersionsUrl = "https://meta.fabricmc.net/v2/versions/installer";
    private const string QuiltGameVersionsUrl = "https://meta.quiltmc.org/v3/versions/game";
    private const string QuiltInstallerVersionsUrl = "https://meta.quiltmc.org/v3/versions/installer";

    public MinecraftService(ILogger<MinecraftService> logger, HttpClient httpClient, ILogService? logService = null)
    {
        _logger = logger;
        _httpClient = httpClient;
        _logService = logService;
    }

    public string GetDefaultGameDirectory(string profileName)
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".minecraft");
    }

    public string GetModsDirectory(string gameDir) => Path.Combine(gameDir, "mods");
    public string GetResourcePacksDirectory(string gameDir) => Path.Combine(gameDir, "resourcepacks");
    public string GetShaderPacksDirectory(string gameDir) => Path.Combine(gameDir, "shaderpacks");

    public async Task<List<GameVersion>> FetchAvailableVersionsAsync()
    {
        try
        {
            _logger.LogInformation("Fetching Minecraft version manifest...");
            var json = await _httpClient.GetStringAsync(VersionManifestUrl);
            var doc = JsonDocument.Parse(json);
            var versions = new List<GameVersion>();

            foreach (var v in doc.RootElement.GetProperty("versions").EnumerateArray())
            {
                versions.Add(new GameVersion
                {
                    VersionId = v.GetProperty("id").GetString() ?? "",
                    VersionType = v.GetProperty("type").GetString() ?? "release",
                    Url = v.GetProperty("url").GetString() ?? "",
                    ReleasedAt = v.TryGetProperty("releaseTime", out var t) ? t.GetDateTime() : DateTime.MinValue
                });
            }
            return versions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch version manifest");
            return new List<GameVersion>();
        }
    }

    public async Task InstallVersionAsync(string versionId, IProgress<double>? progress = null)
    {
        using var operation = _logService?.BeginOperation("MinecraftInstall", "InstallVersion", new { versionId });
        _logger.LogInformation("Installing Minecraft version {Version}", versionId);
        _logService?.Info("MinecraftInstall", "Started", "Instalando versión de Minecraft.", new { versionId });
        var versionData = await FetchVersionDataAsync(versionId);
        if (versionData == null)
            throw new Exception($"Version {versionId} not found");

        var gameDir = GetMinecraftGameDir();
        var versionsDir = Path.Combine(gameDir, "versions", versionId);
        var installMarker = Path.Combine(versionsDir, ".shorocraft-installed.json");
        Directory.CreateDirectory(Path.Combine(gameDir, "versions"));
        if (IsVersionComplete(versionsDir, versionId))
        {
            _logService?.Info("MinecraftInstall", "AlreadyComplete", "La versión ya está instalada.", new { versionId, versionsDir });
            await EnsureLauncherProfileAsync(gameDir, versionId);
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
            await DownloadFileAsync(clientUrl, jarPath, progress);

            var jsonPath = Path.Combine(tempVersionsDir, $"{versionId}.json");
            var versionJson = await _httpClient.GetStringAsync(versionData.Url);
            await File.WriteAllTextAsync(jsonPath, versionJson);

            var libsDir = Path.Combine(gameDir, "libraries");
            var libCount = await DownloadLibrariesAsync(versionData, libsDir, progress);

            if (Directory.Exists(versionsDir))
                Directory.Delete(versionsDir, recursive: true);
            Directory.Move(tempVersionsDir, versionsDir);

            await File.WriteAllTextAsync(installMarker, JsonSerializer.Serialize(new
            {
                versionId,
                installedAt = DateTimeOffset.Now,
                launcher = "ShoroCraftLauncher"
            }, new JsonSerializerOptions { WriteIndented = true }));

            await EnsureLauncherProfileAsync(gameDir, versionId);

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

    public async Task InstallLoaderAsync(string versionId, string loaderType, string loaderVersion, string javaPath, Action<string>? onProgress = null, IProgress<double>? progress = null, Action<string>? onLog = null)
    {
        using var operation = _logService?.BeginOperation("LoaderInstall", "InstallLoader", new { versionId, loaderType, loaderVersion });
        _logger.LogInformation("Installing loader {Loader} for Minecraft {McVersion}", loaderType, versionId);
        _logService?.Info("LoaderInstall", "Started", "Instalando loader.", new { versionId, loaderType, loaderVersion });
        onLog?.Invoke($"[INFO] Preparando instalación de {loaderType} {loaderVersion}...");
        onProgress?.Invoke($"Preparando instalación de {loaderType} {loaderVersion}...");
        
        var gameDir = GetMinecraftGameDir();
        Directory.CreateDirectory(Path.Combine(gameDir, "cache"));

        var versionDir = Path.Combine(gameDir, "versions", versionId);
        if (!Directory.Exists(versionDir) || !File.Exists(Path.Combine(versionDir, $"{versionId}.jar")))
        {
            _logService?.Warning("LoaderInstall", "BaseVersionMissing", "Minecraft base no está instalado; se instalará antes del loader.", new { versionId });
            onLog?.Invoke($"[INFO] Minecraft {versionId} no está instalado. Instalando versión base...");
            onProgress?.Invoke($"Instalando Minecraft {versionId}...");
            await InstallVersionAsync(versionId, progress);
        }

        await EnsureLauncherProfileAsync(gameDir, versionId);

        var installerVersion = loaderType.Equals("fabric", StringComparison.OrdinalIgnoreCase)
            ? await ResolveLatestFabricInstallerVersionAsync()
            : loaderVersion;

        var installerUrl = loaderType.ToLower() switch
        {
            "forge" => $"https://maven.minecraftforge.net/net/minecraftforge/forge/{versionId}-{loaderVersion}/forge-{versionId}-{loaderVersion}-installer.jar",
            "fabric" => $"https://maven.fabricmc.net/net/fabricmc/fabric-installer/{installerVersion}/fabric-installer-{installerVersion}.jar",
            "quilt" => $"https://maven.quiltmc.net/release/org/quiltmc/quilt-installer/{loaderVersion}/quilt-installer-{loaderVersion}.jar",
            _ => throw new Exception($"Unknown loader: {loaderType}")
        };

        var installerPath = Path.Combine(gameDir, "cache", $"{loaderType}-installer-{versionId}-{installerVersion}.jar");
        
        if (!File.Exists(installerPath))
        {
            _logService?.Info("LoaderInstall", "InstallerDownloadStarted", "Descargando instalador de loader.", new { loaderType, installerUrl });
            onLog?.Invoke($"[INFO] Descargando instalador de {loaderType}...");
            onProgress?.Invoke($"Descargando instalador de {loaderType}...");
            try
            {
                await DownloadFileAsync(installerUrl, installerPath, progress);
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
                var javaVer = await jp.StandardError.ReadToEndAsync();
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

        using var process = new Process { StartInfo = psi };
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            AddBoundedLine(outputLines, e.Data);
            if (ShouldEchoLoaderInstallerLine(e.Data))
                onLog?.Invoke($"[{loaderType}] {e.Data}");
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            AddBoundedLine(errorLines, e.Data);
            onLog?.Invoke($"[ERROR] [{loaderType}] {e.Data}");
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(cts.Token);

        if (process.ExitCode != 0)
        {
            var allOutput = string.Join(Environment.NewLine, outputLines.Concat(errorLines));
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

    public async Task<bool> VerifyInstallationAsync(string gameDir)
    {
        return Directory.Exists(Path.Combine(gameDir, "versions"))
            && Directory.Exists(Path.Combine(gameDir, "libraries"))
            && Directory.Exists(Path.Combine(gameDir, "assets"));
    }

    public async Task RepairInstallationAsync(string gameDir, IProgress<double>? progress = null)
    {
        _logger.LogInformation("Repairing installation at {GameDir}", gameDir);
        foreach (var dir in new[] { "versions", "assets", "libraries", "mods", "resourcepacks", "shaderpacks", "saves", "cache", "logs", "natives" })
            Directory.CreateDirectory(Path.Combine(gameDir, dir));
        await Task.CompletedTask;
    }

    public async Task<Process> LaunchGameAsync(Profile profile, string gameDir, string javaPath, string accessToken, string uuid, string username, Action<double, string>? onProgress = null)
    {
        _logger.LogInformation("Launching: profile={Profile}, version={Version}", profile.Name, profile.MinecraftVersion);

        var globalDir = GetMinecraftGameDir();
        var targetVersion = profile.MinecraftVersion;

        if (profile.MinecraftVersion.ToLower() == "latest")
        {
            targetVersion = await ResolveVersionIdAsync("latest");
        }

        if (profile.Type != Core.Enums.ProfileType.Vanilla)
        {
            var loaderPrefix = profile.Type.ToString().ToLower();
            var versionsDir = Path.Combine(globalDir, "versions");
            if (Directory.Exists(versionsDir))
            {
                var match = Directory.GetDirectories(versionsDir)
                    .Select(Path.GetFileName)
                    .FirstOrDefault(n => n != null && n.Contains(loaderPrefix) && n.Contains(targetVersion));
                if (match != null)
                {
                    targetVersion = match;
                }
            }
        }

        onProgress?.Invoke(-1, $"Preparando lanzamiento de {targetVersion}...");

        var mcPath = new CmlLib.Core.MinecraftPath(globalDir);
        var launcher = new CmlLib.Core.MinecraftLauncher(mcPath);

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
            GameLauncherVersion = "1.0.0",
            ScreenWidth = profile.WindowWidth,
            ScreenHeight = profile.WindowHeight,
            FullScreen = profile.IsFullscreen
        };

        var process = await launcher.CreateProcessAsync(targetVersion, launchOption);
        
        process.StartInfo.WorkingDirectory = gameDir;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.Arguments = EnsureNativeAccessModules(process.StartInfo.Arguments);

        return process;
    }

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


    public async Task<string> ResolveVersionIdAsync(string versionId)
    {
        if (versionId.ToLower() != "latest") return versionId;
        try
        {
            var json = await _httpClient.GetStringAsync(VersionManifestUrl);
            var doc = JsonDocument.Parse(json);
            foreach (var v in doc.RootElement.GetProperty("versions").EnumerateArray())
            {
                if (v.GetProperty("type").GetString() == "release")
                    return v.GetProperty("id").GetString() ?? "1.21";
            }
        }
        catch { }
        return "1.21";
    }

    public async Task<string> ResolveLatestLoaderVersionAsync(string loaderType, string mcVersion)
    {
        try
        {
            return loaderType.ToLower() switch
            {
                "forge" => await ResolveLatestForgeVersionAsync(mcVersion),
                "fabric" => await ResolveLatestFabricLoaderVersionAsync(mcVersion),
                "quilt" => await ResolveLatestQuiltInstallerVersionAsync(mcVersion),
                _ => "latest"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve latest {Loader} version for MC {McVersion}", loaderType, mcVersion);
            return "latest";
        }
    }

    private async Task<string> ResolveLatestForgeVersionAsync(string mcVersion)
    {
        var json = await _httpClient.GetStringAsync(ForgePromotionsUrl);
        var doc = JsonDocument.Parse(json);
        var promos = doc.RootElement.GetProperty("promos");

        if (promos.TryGetProperty($"{mcVersion}-recommended", out var rec))
            return rec.GetString() ?? "latest";

        if (promos.TryGetProperty($"{mcVersion}-latest", out var lat))
            return lat.GetString() ?? "latest";

        _logger.LogWarning("No Forge version found for MC {McVersion}, falling back to 'latest'", mcVersion);
        return "latest";
    }

    private async Task<string> ResolveLatestFabricLoaderVersionAsync(string mcVersion)
    {
        if (!await FabricSupportsGameVersionAsync(mcVersion))
            throw new Exception($"Fabric no reporta soporte para Minecraft {mcVersion}.");

        var json = await _httpClient.GetStringAsync(FabricLoaderVersionsUrl);
        var doc = JsonDocument.Parse(json);
        return doc.RootElement[0].GetProperty("version").GetString() ?? "latest";
    }

    private async Task<string> ResolveLatestFabricInstallerVersionAsync()
    {
        var json = await _httpClient.GetStringAsync(FabricInstallerVersionsUrl);
        var doc = JsonDocument.Parse(json);
        return doc.RootElement[0].GetProperty("version").GetString() ?? "latest";
    }

    private async Task<string> ResolveLatestQuiltInstallerVersionAsync(string mcVersion)
    {
        if (!await QuiltSupportsGameVersionAsync(mcVersion))
            throw new Exception($"Quilt no reporta soporte para Minecraft {mcVersion}.");

        var json = await _httpClient.GetStringAsync(QuiltInstallerVersionsUrl);
        var doc = JsonDocument.Parse(json);
        return doc.RootElement[0].GetProperty("version").GetString() ?? "latest";
    }

    private async Task<bool> FabricSupportsGameVersionAsync(string mcVersion)
    {
        var json = await _httpClient.GetStringAsync(FabricGameVersionsUrl);
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray().Any(v =>
            string.Equals(v.GetProperty("version").GetString(), mcVersion, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> QuiltSupportsGameVersionAsync(string mcVersion)
    {
        var json = await _httpClient.GetStringAsync(QuiltGameVersionsUrl);
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray().Any(v =>
            string.Equals(v.GetProperty("version").GetString(), mcVersion, StringComparison.OrdinalIgnoreCase));
    }

    private string BuildClassPath(string globalDir, string gameDir, string versionId)
    {
        var entries = new List<string>();

        var jarPath = Path.Combine(globalDir, "versions", versionId, $"{versionId}.jar");
        if (File.Exists(jarPath)) entries.Add(jarPath);

        var libsDir = Path.Combine(globalDir, "libraries");
        if (Directory.Exists(libsDir))
        {
            foreach (var lib in Directory.GetFiles(libsDir, "*.jar", SearchOption.AllDirectories))
                entries.Add(lib);
        }

        var modsDir = Path.Combine(gameDir, "mods");
        if (Directory.Exists(modsDir))
        {
            foreach (var mod in Directory.GetFiles(modsDir, "*.jar"))
                entries.Add(mod);
        }

        return string.Join(";", entries);
    }

    private async Task<VersionData?> FetchVersionDataAsync(string versionId)
    {
        try
        {
            var manifestJson = await _httpClient.GetStringAsync(VersionManifestUrl);
            var manifest = JsonDocument.Parse(manifestJson);
            string? versionUrl = null;

            foreach (var v in manifest.RootElement.GetProperty("versions").EnumerateArray())
            {
                if (v.GetProperty("id").GetString() == versionId)
                {
                    versionUrl = v.GetProperty("url").GetString();
                    break;
                }
            }

            if (versionUrl == null) return null;

            var versionJson = await _httpClient.GetStringAsync(versionUrl);
            return new VersionData(versionId, versionUrl, versionJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch version data for {Version}", versionId);
            return null;
        }
    }

    private async Task<int> DownloadLibrariesAsync(VersionData versionData, string libsDir, IProgress<double>? progress)
    {
        int count = 0;
        var libs = versionData.GetLibraries();
        for (int i = 0; i < libs.Count; i++)
        {
            var lib = libs[i];
            if (lib.Path == null || lib.Url == null) continue;

            var destPath = Path.Combine(libsDir, lib.Path);
            if (File.Exists(destPath)) continue;

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            try
            {
                await DownloadFileAsync(lib.Url, destPath, null);
                count++;
                progress?.Report((double)(i + 1) / libs.Count * 100);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download library {Lib}", lib.Path);
            }
        }
        return count;
    }

    private async Task DownloadFileAsync(string url, string destinationPath, IProgress<double>? progress)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        using var contentStream = await response.Content.ReadAsStreamAsync();
        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

        if (totalBytes > 0)
        {
            var buffer = new byte[8192];
            long readBytes = 0;
            int read;
            while ((read = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read);
                readBytes += read;
                progress?.Report((double)readBytes / totalBytes * 100);
            }
        }
        else
        {
            await contentStream.CopyToAsync(fileStream);
        }
    }

    private static string GetMinecraftGameDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");

    private static bool IsVersionComplete(string versionsDir, string versionId)
    {
        return Directory.Exists(versionsDir)
            && File.Exists(Path.Combine(versionsDir, $"{versionId}.jar"))
            && File.Exists(Path.Combine(versionsDir, $"{versionId}.json"))
            && File.Exists(Path.Combine(versionsDir, ".shorocraft-installed.json"));
    }

    private static async Task EnsureLauncherProfileAsync(string gameDir, string versionId)
    {
        Directory.CreateDirectory(gameDir);

        var profilesPath = Path.Combine(gameDir, "launcher_profiles.json");
        JsonObject root;

        if (File.Exists(profilesPath))
        {
            try
            {
                root = JsonNode.Parse(await File.ReadAllTextAsync(profilesPath))?.AsObject() ?? new JsonObject();
            }
            catch
            {
                root = new JsonObject();
            }
        }
        else
        {
            root = new JsonObject();
        }

        var profiles = root["profiles"] as JsonObject ?? new JsonObject();
        root["profiles"] = profiles;

        var now = DateTimeOffset.UtcNow.ToString("O");
        var profile = profiles["ShoroCraft"] as JsonObject ?? new JsonObject();
        profile["name"] = "ShoroCraft";
        profile["type"] = "custom";
        profile["created"] ??= now;
        profile["lastUsed"] = now;
        profile["lastVersionId"] = versionId;
        profiles["ShoroCraft"] = profile;

        root["selectedProfile"] = "ShoroCraft";
        root["clientToken"] ??= Guid.NewGuid().ToString("N");
        root["authenticationDatabase"] ??= new JsonObject();
        root["settings"] ??= new JsonObject();
        root["version"] ??= 3;
        root["launcherVersion"] ??= new JsonObject
        {
            ["name"] = "ShoroCraft Launcher",
            ["format"] = 21
        };

        await File.WriteAllTextAsync(
            profilesPath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string SanitizeFolderName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    private async Task ExtractNativesAsync(VersionData versionData, string gameDir, string nativesDir)
    {
        _logger.LogInformation("Extracting native libraries...");

        var nativeCacheDir = Path.Combine(gameDir, "cache", "natives");
        Directory.CreateDirectory(nativeCacheDir);
        var osName = GetCurrentOsName();
        var nativeEntries = versionData.GetNativeLibraries(osName);

        if (nativeEntries.Count == 0)
        {
            _logger.LogWarning("No native libraries found for OS {Os}", osName);
            return;
        }

        foreach (var native in nativeEntries)
        {
            var jarName = Path.GetFileName(native.Path);
            var destPath = Path.Combine(nativeCacheDir, jarName);
            if (!File.Exists(destPath))
            {
                _logger.LogInformation("Downloading native library: {Jar}", jarName);
                await DownloadFileAsync(native.Url, destPath, null);
            }

            _logger.LogDebug("Extracting natives from {Jar}", jarName);
            try
            {
                using var archive = ZipFile.OpenRead(destPath);
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                        || entry.FullName.EndsWith(".so", StringComparison.OrdinalIgnoreCase)
                        || entry.FullName.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase)
                        || entry.FullName.EndsWith(".jnilib", StringComparison.OrdinalIgnoreCase))
                    {
                        var extractPath = Path.Combine(nativesDir, Path.GetFileName(entry.FullName));
                        if (!File.Exists(extractPath))
                        {
                            entry.ExtractToFile(extractPath, overwrite: true);
                            _logger.LogTrace("Extracted native: {File}", entry.FullName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract natives from {Jar}", jarName);
            }
        }

        _logger.LogInformation("Native extraction complete");
    }

    private async Task DownloadAssetsAsync(VersionData versionData, string gameDir)
    {
        var assetIndexUrl = versionData.GetAssetIndexUrl();
        var assetIndexId = versionData.GetAssetIndexId();
        if (string.IsNullOrEmpty(assetIndexUrl))
        {
            _logger.LogWarning("No asset index URL for version {Version}", versionData.Id);
            return;
        }

        var indexesDir = Path.Combine(gameDir, "assets", "indexes");
        Directory.CreateDirectory(indexesDir);
        var indexPath = Path.Combine(indexesDir, $"{assetIndexId}.json");

        if (!File.Exists(indexPath))
        {
            _logger.LogInformation("Downloading asset index {AssetIndexId}...", assetIndexId);
            var indexJson = await _httpClient.GetStringAsync(assetIndexUrl);
            await File.WriteAllTextAsync(indexPath, indexJson);
        }

        var assetsDir = Path.Combine(gameDir, "assets", "objects");
        Directory.CreateDirectory(assetsDir);

        var indexDoc = JsonDocument.Parse(await File.ReadAllTextAsync(indexPath));
        if (!indexDoc.RootElement.TryGetProperty("objects", out var objects)) return;

        var total = objects.EnumerateObject().Count();
        var count = 0;
        foreach (var obj in objects.EnumerateObject())
        {
            var hash = obj.Value.GetProperty("hash").GetString();
            if (string.IsNullOrEmpty(hash)) continue;

            var hashPrefix = hash[..2];
            var objectDir = Path.Combine(assetsDir, hashPrefix);
            Directory.CreateDirectory(objectDir);
            var objectPath = Path.Combine(objectDir, hash);

            if (!File.Exists(objectPath))
            {
                var assetUrl = $"https://resources.download.minecraft.net/{hashPrefix}/{hash}";
                try
                {
                    await DownloadFileAsync(assetUrl, objectPath, null);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to download asset {Hash}", hash);
                }
            }

            count++;
            if (count % 100 == 0)
                _logger.LogDebug("Downloaded {Count}/{Total} assets", count, total);
        }

        _logger.LogInformation("Asset download complete: {Count} assets", count);
    }

    private static string GetCurrentOsName()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "osx";
        return "linux";
    }

    private class VersionData
    {
        private readonly JsonDocument _doc;
        public string Id { get; }
        public string Url { get; }

        public VersionData(string id, string url, string json)
        {
            Id = id;
            Url = url;
            _doc = JsonDocument.Parse(json);
        }

        public string? GetClientUrl()
        {
            if (_doc.RootElement.TryGetProperty("downloads", out var downloads)
                && downloads.TryGetProperty("client", out var client)
                && client.TryGetProperty("url", out var url))
                return url.GetString();
            return null;
        }

        public string GetMainClass()
        {
            if (_doc.RootElement.TryGetProperty("mainClass", out var mc))
                return mc.GetString() ?? "net.minecraft.client.main.Main";
            return "net.minecraft.client.main.Main";
        }

        public string GetAssetIndexId()
        {
            if (_doc.RootElement.TryGetProperty("assetIndex", out var ai)
                && ai.TryGetProperty("id", out var id))
                return id.GetString() ?? "1.21";
            return "1.21";
        }

        public string? GetAssetIndexUrl()
        {
            if (_doc.RootElement.TryGetProperty("assetIndex", out var ai)
                && ai.TryGetProperty("url", out var url))
                return url.GetString();
            return null;
        }

        public List<(string? Path, string? Url)> GetLibraries()
        {
            var result = new List<(string?, string?)>();
            if (!_doc.RootElement.TryGetProperty("libraries", out var libs)) return result;

            foreach (var lib in libs.EnumerateArray())
            {
                if (!LibraryPassesRules(lib)) continue;
                if (lib.TryGetProperty("downloads", out var dl)
                    && dl.TryGetProperty("artifact", out var artifact))
                {
                    var path = artifact.TryGetProperty("path", out var p) ? p.GetString() : null;
                    var url = artifact.TryGetProperty("url", out var u) ? u.GetString() : null;
                    result.Add((path, url));
                }
            }
            return result;
        }

        public List<(string Path, string Url)> GetNativeLibraries(string osName)
        {
            var result = new List<(string, string)>();
            if (!_doc.RootElement.TryGetProperty("libraries", out var libs)) return result;

            foreach (var lib in libs.EnumerateArray())
            {
                if (!LibraryPassesRules(lib)) continue;
                if (!lib.TryGetProperty("natives", out var natives)) continue;
                if (!natives.TryGetProperty(osName, out var classifierElement)) continue;
                var classifier = classifierElement.GetString();
                if (classifier == null) continue;

                if (!lib.TryGetProperty("downloads", out var dl)) continue;
                if (!dl.TryGetProperty("classifiers", out var classifiers)) continue;
                if (!classifiers.TryGetProperty(classifier, out var nativeEntry)) continue;

                var path = nativeEntry.TryGetProperty("path", out var p) ? p.GetString() : null;
                var url = nativeEntry.TryGetProperty("url", out var u) ? u.GetString() : null;
                if (path != null && url != null)
                    result.Add((path, url));
            }
            return result;
        }

        private static bool LibraryPassesRules(JsonElement lib)
        {
            if (!lib.TryGetProperty("rules", out var rules)) return true;

            bool allowed = false;
            foreach (var rule in rules.EnumerateArray())
            {
                var action = rule.GetProperty("action").GetString();
                bool matches = true;

                if (rule.TryGetProperty("os", out var os))
                {
                    if (os.TryGetProperty("name", out var osName))
                        matches &= osName.GetString() == GetCurrentOsName();

                    if (os.TryGetProperty("arch", out var arch))
                    {
                        var is64Bit = Environment.Is64BitOperatingSystem;
                        matches &= (arch.GetString() == "x86" && !is64Bit)
                                || (arch.GetString() == "x86_64" && is64Bit);
                    }
                }

                if (action == "allow" && matches) allowed = true;
                else if (action == "disallow" && matches) return false;
            }
            return allowed;
        }
    }
}
