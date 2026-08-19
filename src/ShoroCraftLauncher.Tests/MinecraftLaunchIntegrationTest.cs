using Microsoft.Extensions.Logging;
using Moq;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Infrastructure.Minecraft;
using ShoroCraftLauncher.Infrastructure.Authentication;
using ShoroCraftLauncher.Infrastructure.Services;
using Xunit;

namespace ShoroCraftLauncher.Tests;

public class MinecraftLaunchIntegrationTest
{
    private readonly ILogger<MinecraftService> _logger = Mock.Of<ILogger<MinecraftService>>();
    private readonly ILogger<JavaService> _javaLogger = Mock.Of<ILogger<JavaService>>();
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    [Trait("Category", "Integration")]
    [ManualIntegrationFact]
    public async Task FetchVersionManifest_ReturnsVersions()
    {
        var mc = new MinecraftService(_logger, _httpClient);
        var versions = await mc.FetchAvailableVersionsAsync();

        Assert.NotEmpty(versions);
        Assert.Contains(versions, v => v.VersionType == "release");
    }

    [Trait("Category", "Integration")]
    [ManualIntegrationFact]
    public async Task ResolveLatest_ReturnsRealVersion()
    {
        var mc = new MinecraftService(_logger, _httpClient);
        var versions = await mc.FetchAvailableVersionsAsync();
        var latest = versions.FirstOrDefault(v => v.VersionType == "release");

        Assert.NotNull(latest);
        Assert.False(string.IsNullOrEmpty(latest.VersionId));
    }

    [Trait("Category", "Integration")]
    [ManualIntegrationFact]
    public async Task InstallVersion1_21_4_DownloadsClientAndLibraries()
    {
        var mc = new MinecraftService(_logger, _httpClient);
        var versionId = "1.21.4";
        var versionsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".minecraft", "versions", versionId);
        var libsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".minecraft", "libraries");

        if (Directory.Exists(versionsDir)) Directory.Delete(versionsDir, true);

        await mc.InstallVersionAsync(versionId);

        var jarPath = Path.Combine(versionsDir, $"{versionId}.jar");
        Assert.True(File.Exists(jarPath), $"Client jar not found at {jarPath}");

        var jsonPath = Path.Combine(versionsDir, $"{versionId}.json");
        Assert.True(File.Exists(jsonPath), $"Version json not found at {jsonPath}");

        var libFiles = Directory.GetFiles(libsDir, "*.jar", SearchOption.AllDirectories);
        Assert.NotEmpty(libFiles);
    }

    [Trait("Category", "Integration")]
    [ManualIntegrationFact]
    public async Task JavaService_FindsJavaInstallation()
    {
        var java = new JavaService(_javaLogger, _httpClient);
        var installations = await java.FindJavaInstallationsAsync();

        Assert.NotEmpty(installations);
        var valid = installations.Where(i => i.IsValid).ToList();
        Assert.NotEmpty(valid);

        var recommended = await java.GetRecommendedJavaPathAsync("1.21");
        Assert.False(string.IsNullOrEmpty(recommended));
        Assert.True(File.Exists(recommended));
    }

    [Trait("Category", "Integration")]
    [ManualIntegrationFact]
    public async Task LaunchBuild_RunsWithoutCrashing()
    {
        var java = new JavaService(_javaLogger, _httpClient);
        var auth = new AuthenticationService(Mock.Of<ILogger<AuthenticationService>>());
        var mc = new MinecraftService(_logger, _httpClient);
        var launcher = new LauncherService(mc, java, auth, Mock.Of<ILogger<LauncherService>>());

        var javaPath = await java.GetRecommendedJavaPathAsync("1.21.4");
        if (string.IsNullOrEmpty(javaPath))
        {
            Assert.Fail("Java not found - install Java 17+ to run this test");
            return;
        }

        var testGameDir = Path.Combine(Path.GetTempPath(), "ShoroCraftTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testGameDir);

        try
        {
            var profile = new Profile
            {
                Name = "TestProfile",
                MinecraftVersion = "1.21.4",
                MinRamMB = 1024,
                MaxRamMB = 2048,
                WindowWidth = 854,
                WindowHeight = 480,
                GameDirectory = testGameDir
            };

            var authResult = auth.AuthenticateOfflineAsync("TestPlayer");
            Assert.True(authResult.Success);

            var result = await launcher.LaunchProfileAsync(profile, authResult);
            Assert.True(result.Success, $"Launch failed: {result.ErrorMessage}");

            await Task.Delay(TimeSpan.FromSeconds(10));
            await launcher.StopGameAsync();
        }
        finally
        {
            try { Directory.Delete(testGameDir, true); } catch { }
        }
    }
}
