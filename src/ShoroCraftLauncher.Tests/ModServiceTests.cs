using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Moq;
using ShoroCraftLauncher.Core;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Infrastructure.Downloading;
using ShoroCraftLauncher.Infrastructure.Services;
using Xunit;

namespace ShoroCraftLauncher.Tests;

public class ModServiceTests
{
    private static readonly ILogger<ModService> Logger = Mock.Of<ILogger<ModService>>();
    private static readonly Mock<ILogService> LogService = new(MockBehavior.Loose);

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    private static ModService CreateService(
        Mock<IModRepository>? modRepo = null,
        Mock<IProfileRepository>? profileRepo = null,
        Mock<ISettingsRepository>? settingsRepo = null,
        Mock<IMinecraftService>? minecraft = null,
        HttpClient? httpClient = null)
    {
        var searchService = new ModSearchService(
            settingsRepo?.Object ?? Mock.Of<ISettingsRepository>(),
            Mock.Of<ILogger<ModSearchService>>(),
            LogService.Object,
            httpClient ?? new HttpClient());

        return new ModService(
            modRepo?.Object ?? Mock.Of<IModRepository>(),
            profileRepo?.Object ?? Mock.Of<IProfileRepository>(),
            settingsRepo?.Object ?? Mock.Of<ISettingsRepository>(),
            minecraft?.Object ?? Mock.Of<IMinecraftService>(),
            searchService,
            Logger,
            LogService.Object,
            httpClient ?? new HttpClient());
    }

    [Fact]
    public async Task ToggleModAsync_EnablesInactiveMod()
    {
        var tempDir = TestPaths.CreateTempDir("ShoroModToggle");
        try
        {
            var modPath = Path.Combine(tempDir, "testmod.jar");
            var disabledPath = modPath + ".disabled";
            await File.WriteAllBytesAsync(disabledPath, new byte[] { 0x01 });

            var mod = new Mod { Id = 1, FilePath = disabledPath, Status = ModStatus.Inactive };

            var mockRepo = new Mock<IModRepository>(MockBehavior.Strict);
            mockRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(mod);
            mockRepo.Setup(x => x.UpdateAsync(It.IsAny<Mod>())).Returns(Task.CompletedTask);

            var service = CreateService(modRepo: mockRepo);

            await service.ToggleModAsync(1);

            Assert.True(File.Exists(modPath));
            Assert.False(File.Exists(disabledPath));
            mockRepo.Verify(x => x.UpdateAsync(It.Is<Mod>(m => m.Status == ModStatus.Active)), Times.Once);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task ToggleModAsync_DisablesActiveMod()
    {
        var tempDir = TestPaths.CreateTempDir("ShoroModToggleOff");
        try
        {
            var modPath = Path.Combine(tempDir, "testmod.jar");
            var disabledPath = modPath + ".disabled";
            await File.WriteAllBytesAsync(modPath, new byte[] { 0x01 });

            var mod = new Mod { Id = 2, FilePath = modPath, Status = ModStatus.Active };

            var mockRepo = new Mock<IModRepository>(MockBehavior.Strict);
            mockRepo.Setup(x => x.GetByIdAsync(2)).ReturnsAsync(mod);
            mockRepo.Setup(x => x.UpdateAsync(It.IsAny<Mod>())).Returns(Task.CompletedTask);

            var service = CreateService(modRepo: mockRepo);

            await service.ToggleModAsync(2);

            Assert.True(File.Exists(disabledPath));
            mockRepo.Verify(x => x.UpdateAsync(It.Is<Mod>(m => m.Status == ModStatus.Inactive)), Times.Once);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task AddModAsync_CopiesFileAndCreatesModRecord()
    {
        var tempDir = TestPaths.CreateTempDir("ShoroModAdd");
        try
        {
            var sourceFile = Path.Combine(tempDir, "source.jar");
            await File.WriteAllBytesAsync(sourceFile, new byte[] { 0xAA, 0xBB });

            var profile = new Profile
            {
                Id = 1,
                Name = "TestProfile",
                MinecraftVersion = "1.21.4",
                Type = ProfileType.Vanilla,
                GameDirectory = tempDir
            };

            var modsDir = Path.Combine(tempDir, "mods");
            Directory.CreateDirectory(modsDir);

            var mockProfileRepo = new Mock<IProfileRepository>(MockBehavior.Strict);
            mockProfileRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(profile);

            var mockMinecraft = new Mock<IMinecraftService>(MockBehavior.Strict);
            mockMinecraft.Setup(x => x.GetModsDirectory(tempDir)).Returns(modsDir);

            var mockModRepo = new Mock<IModRepository>(MockBehavior.Strict);
            mockModRepo.Setup(x => x.CreateAsync(It.IsAny<Mod>())).ReturnsAsync((Mod m) => { m.Id = 1; return m.Id; });

            var service = CreateService(profileRepo: mockProfileRepo, modRepo: mockModRepo, minecraft: mockMinecraft);

            var result = await service.AddModAsync(1, sourceFile);

            Assert.Equal("source.jar", result.FileName);
            Assert.True(File.Exists(Path.Combine(modsDir, "source.jar")));
            mockModRepo.Verify(x => x.CreateAsync(It.Is<Mod>(m =>
                m.FileName == "source.jar" && m.Status == ModStatus.Active)), Times.Once);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task AddModAsync_ThrowsOnNonJarFile()
    {
        var tempDir = TestPaths.CreateTempDir("ShoroModAddNonJar");
        try
        {
            var sourceFile = Path.Combine(tempDir, "readme.txt");
            await File.WriteAllTextAsync(sourceFile, "not a jar");

            var profile = new Profile { Id = 1, GameDirectory = tempDir };
            var mockProfileRepo = new Mock<IProfileRepository>(MockBehavior.Strict);
            mockProfileRepo.Setup(x => x.GetByIdAsync(1)).ReturnsAsync(profile);

            var service = CreateService(profileRepo: mockProfileRepo);

            await Assert.ThrowsAsync<Exception>(() => service.AddModAsync(1, sourceFile));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task RemoveModAsync_DeletesFileAndRemovesFromRepo()
    {
        var tempDir = TestPaths.CreateTempDir("ShoroModRemove");
        try
        {
            var modPath = Path.Combine(tempDir, "badmod.jar");
            await File.WriteAllBytesAsync(modPath, new byte[] { 0x01 });

            var mod = new Mod { Id = 5, FilePath = modPath, Status = ModStatus.Active };

            var mockRepo = new Mock<IModRepository>(MockBehavior.Strict);
            mockRepo.Setup(x => x.GetByIdAsync(5)).ReturnsAsync(mod);
            mockRepo.Setup(x => x.DeleteAsync(5)).Returns(Task.CompletedTask);

            var service = CreateService(modRepo: mockRepo);

            await service.RemoveModAsync(5);

            Assert.False(File.Exists(modPath));
            mockRepo.Verify(x => x.DeleteAsync(5), Times.Once);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task GetModsAsync_ReturnsProfileMods()
    {
        var mods = new List<Mod>
        {
            new() { Id = 1, Name = "Fabric API", Status = ModStatus.Active },
            new() { Id = 2, Name = "Sodium", Status = ModStatus.Inactive }
        };

        var mockRepo = new Mock<IModRepository>(MockBehavior.Strict);
        mockRepo.Setup(x => x.GetByProfileIdAsync(1)).ReturnsAsync(mods);

        var service = CreateService(modRepo: mockRepo);
        var result = await service.GetModsAsync(1);

        Assert.Equal(2, result.Count);
        Assert.Equal("Fabric API", result[0].Name);
        mockRepo.Verify(x => x.GetByProfileIdAsync(1), Times.Once);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsEmptyWhenNoActiveMods()
    {
        var mockRepo = new Mock<IModRepository>(MockBehavior.Strict);
        mockRepo.Setup(x => x.GetByProfileIdAsync(1)).ReturnsAsync(new List<Mod>());

        var service = CreateService(modRepo: mockRepo);
        var updates = await service.CheckForUpdatesAsync(1, "1.21.4");

        Assert.Empty(updates);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_FiltersOutInactiveAndNonRemoteMods()
    {
        var mods = new List<Mod>
        {
            new() { Id = 1, Name = "ActiveRemote", Status = ModStatus.Active, RemoteProjectId = "P7dR8mSH", RemoteSlug = "fabric-api", ModVersion = "0.90.0", FileName = "fabric-api.jar" },
            new() { Id = 2, Name = "InactiveRemote", Status = ModStatus.Inactive, RemoteProjectId = "P7dR8mSH", RemoteSlug = "fabric-api", FileName = "fabric-api.jar" },
            new() { Id = 3, Name = "LocalOnly", Status = ModStatus.Active, FileName = "localmod.jar" }
        };

        var mockRepo = new Mock<IModRepository>(MockBehavior.Strict);
        mockRepo.Setup(x => x.GetByProfileIdAsync(1)).ReturnsAsync(mods);
        mockRepo.Setup(x => x.UpdateAsync(It.IsAny<Mod>())).Returns(Task.CompletedTask);

        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(handler);

        var service = CreateService(modRepo: mockRepo, httpClient: httpClient);
        var updates = await service.CheckForUpdatesAsync(1, "1.21.4");

        Assert.Empty(updates);
    }

    [Fact]
    public async Task ExtractModInfoAsync_ReturnsNullForNonJar()
    {
        var tempDir = TestPaths.CreateTempDir("ShoroModExtract");
        try
        {
            var textFile = Path.Combine(tempDir, "readme.txt");
            await File.WriteAllTextAsync(textFile, "hello");

            var mockRepo = new Mock<IModRepository>(MockBehavior.Strict);
            mockRepo.Setup(x => x.GetByProfileIdAsync(1)).ReturnsAsync(new List<Mod>());

            var service = CreateService(modRepo: mockRepo);
            var info = await service.ExtractModInfoAsync(textFile);

            Assert.Null(info.Name);
            Assert.Null(info.MinecraftVersion);
            Assert.Null(info.ModVersion);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MinecraftVersions_ToModrinthVersion_Maps26xCorrectly()
    {
        Assert.Equal("1.21.1", MinecraftVersions.ToModrinthVersion("26.1.1"));
        Assert.Equal("1.21.4", MinecraftVersions.ToModrinthVersion("26.4"));
        Assert.Equal("1.21.4", MinecraftVersions.ToModrinthVersion("1.21.4"));
        Assert.Equal("1.20.6", MinecraftVersions.ToModrinthVersion("1.20.6"));
        Assert.Equal("latest", MinecraftVersions.ToModrinthVersion("latest"));
    }
}
