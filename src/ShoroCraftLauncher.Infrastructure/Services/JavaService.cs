using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.Core.Interfaces;

namespace ShoroCraftLauncher.Infrastructure.Services;

public class JavaService : IJavaService
{
    private const string VersionManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    private readonly ILogger<JavaService> _logger;
    private readonly HttpClient _httpClient;

    public JavaService(ILogger<JavaService> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<List<JavaInfo>> FindJavaInstallationsAsync()
    {
        var installations = new List<JavaInfo>();

        var searchPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Eclipse Adoptium"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Java"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Eclipse Adoptium"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Adoptium"),
            @"C:\Program Files\Amazon Corretto",
            @"C:\Program Files\BellSoft\LibericaJDK",
        };

        foreach (var basePath in searchPaths)
        {
            if (!Directory.Exists(basePath)) continue;

            foreach (var dir in Directory.GetDirectories(basePath, "*", SearchOption.AllDirectories))
            {
                await TryAddJavaFromDir(dir, installations);
            }
        }

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
        {
            var info = await GetJavaInfoFromDir(Path.Combine(javaHome, "bin"));
            if (info != null && !installations.Any(i => i.Path.Equals(info.Path, StringComparison.OrdinalIgnoreCase)))
                installations.Add(info);
        }

        var pathJava = FindJavaInPath();
        if (pathJava != null)
        {
            var info = await GetJavaInfoAsync(pathJava);
            if (info != null && !installations.Any(i => i.Path.Equals(info.Path, StringComparison.OrdinalIgnoreCase)))
                installations.Add(info);
        }

        return installations;
    }

    public async Task<string> GetRecommendedJavaPathAsync(string minecraftVersion)
    {
        var installations = await FindJavaInstallationsAsync();
        var valid = installations.Where(i => i.IsValid).ToList();

        if (valid.Count == 0)
            return string.Empty;

        var recommendedJavaVersion = await GetRecommendedJavaMajorAsync(minecraftVersion);

        var withMajor = valid
            .Select(j => (Info: j, Major: ParseVersion(j.Version)))
            .Where(j => j.Major >= 8)
            .OrderBy(j => j.Major)
            .ToList();

        if (withMajor.Count == 0)
            return valid[0].Path;

        var best = withMajor.FirstOrDefault(j => j.Major == recommendedJavaVersion);
        if (best.Info != null)
            return best.Info.Path;

        best = withMajor.FirstOrDefault(j => j.Major >= recommendedJavaVersion);
        if (best.Info != null)
            return best.Info.Path;

        return withMajor.Last().Info.Path;
    }

    public async Task<string> DownloadJavaForVersionAsync(string minecraftVersion, IProgress<double>? progress = null)
    {
        _logger.LogInformation("Downloading Java for Minecraft {Version}", minecraftVersion);

        var major = await GetRecommendedJavaMajorAsync(minecraftVersion);
        var component = major >= 21 ? "java-runtime-delta" : major >= 17 ? "java-runtime-alpha" : "jre-legacy";

        var minecraftPath = new CmlLib.Core.MinecraftPath();
        var path = new CmlLib.Core.Java.MinecraftJavaPathResolver(minecraftPath);
        var javaVer = new CmlLib.Core.Java.JavaVersion(component);

        try
        {
            var binPath = path.GetJavaBinaryPath(javaVer, new CmlLib.Core.Rules.RulesEvaluatorContext(CmlLib.Core.Rules.LauncherOSRule.Current));
            if (File.Exists(binPath))
                return binPath;
        }
        catch { }

        try
        {
            var launcher = new CmlLib.Core.MinecraftLauncher(minecraftPath);
            await launcher.InstallAsync(minecraftVersion);
            var binPath = path.GetJavaBinaryPath(javaVer, new CmlLib.Core.Rules.RulesEvaluatorContext(CmlLib.Core.Rules.LauncherOSRule.Current));
            return File.Exists(binPath) ? binPath : string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download Java for component {Component}", component);
            return string.Empty;
        }
    }

    public async Task<JavaInfo?> DownloadJavaAsync(IProgress<double>? progress = null)
    {
        // Redirect to a default (e.g. 17)
        var path = await DownloadJavaForVersionAsync("1.17", progress);
        return await GetJavaInfoAsync(path);
    }

    private async Task<JavaInfo?> GetJavaInfoAsync(string javaPath)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = javaPath,
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0) return null;

            var match = Regex.Match(output, @"(?:openjdk|java) version ""([^""]+)""");
            if (!match.Success) return null;

            var versionStr = match.Groups[1].Value;
            var majorVersion = ParseVersion(versionStr);

            return new JavaInfo
            {
                Path = javaPath,
                Version = versionStr,
                IsValid = majorVersion >= 8
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task TryAddJavaFromDir(string dir, List<JavaInfo> installations)
    {
        foreach (var exe in new[] { "javaw.exe", "java.exe" })
        {
            var javaPath = Path.Combine(dir, "bin", exe);
            if (!File.Exists(javaPath)) continue;

            var info = await GetJavaInfoAsync(javaPath);
            if (info != null && !installations.Any(i => i.Path.Equals(info.Path, StringComparison.OrdinalIgnoreCase)))
            {
                installations.Add(info);
                return;
            }
        }
    }

    private async Task<JavaInfo?> GetJavaInfoFromDir(string binDir)
    {
        foreach (var exe in new[] { "javaw.exe", "java.exe" })
        {
            var javaPath = Path.Combine(binDir, exe);
            if (!File.Exists(javaPath)) continue;
            return await GetJavaInfoAsync(javaPath);
        }
        return null;
    }

    private string? FindJavaInPath()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;

        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            try
            {
                foreach (var exe in new[] { "javaw.exe", "java.exe" })
                {
                    var javaPath = Path.Combine(dir.Trim(), exe);
                    if (File.Exists(javaPath)) return javaPath;
                }
            }
            catch { }
        }
        return null;
    }

    private static int ParseVersion(string version)
    {
        if (version.StartsWith("1."))
        {
            if (int.TryParse(version.AsSpan(2, version.IndexOf('.', 2) > 2 ? version.IndexOf('.', 2) - 2 : 1), out var v))
                return v;
            return 0;
        }

        var dotIndex = version.IndexOf('.');
        if (dotIndex > 0 && int.TryParse(version[..dotIndex], out var major))
            return major;

        if (int.TryParse(version, out major))
            return major;

        return 0;
    }

    private async Task<int> GetRecommendedJavaMajorAsync(string minecraftVersion)
    {
        try
        {
            var json = await _httpClient.GetStringAsync(VersionManifestUrl);
            using var manifest = JsonDocument.Parse(json);
            var resolvedVersion = minecraftVersion;

            if (minecraftVersion.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                resolvedVersion = manifest.RootElement
                    .GetProperty("latest")
                    .GetProperty("release")
                    .GetString() ?? minecraftVersion;
            }

            foreach (var version in manifest.RootElement.GetProperty("versions").EnumerateArray())
            {
                if (!string.Equals(version.GetProperty("id").GetString(), resolvedVersion, StringComparison.OrdinalIgnoreCase))
                    continue;

                var versionUrl = version.GetProperty("url").GetString();
                if (string.IsNullOrWhiteSpace(versionUrl))
                    break;

                var versionJson = await _httpClient.GetStringAsync(versionUrl);
                using var versionDoc = JsonDocument.Parse(versionJson);
                if (versionDoc.RootElement.TryGetProperty("javaVersion", out var javaVersion)
                    && javaVersion.TryGetProperty("majorVersion", out var major)
                    && major.TryGetInt32(out var manifestMajor)
                    && manifestMajor >= 8)
                {
                    return manifestMajor;
                }

                break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve Java version from Minecraft manifest for {Version}", minecraftVersion);
        }

        return GetFallbackJavaMajor(minecraftVersion);
    }

    private static int GetFallbackJavaMajor(string minecraftVersion)
    {
        if (!TryParseMinecraftVersion(minecraftVersion, out var major, out var minor, out var patch))
            return 17;

        if (major >= 26)
            return 25;

        if (major > 1)
            return 21;

        if (minor >= 21 || (minor == 20 && patch >= 5))
            return 21;

        if (minor >= 17)
            return 17;

        return 8;
    }

    private static bool TryParseMinecraftVersion(string version, out int major, out int minor, out int patch)
    {
        major = 0;
        minor = 0;
        patch = 0;

        var match = Regex.Match(version, @"^(\d+)\.(\d+)(?:\.(\d+))?");
        if (!match.Success)
            return false;

        major = int.Parse(match.Groups[1].Value);
        minor = int.Parse(match.Groups[2].Value);
        if (match.Groups[3].Success)
            patch = int.Parse(match.Groups[3].Value);

        return true;
    }
}
