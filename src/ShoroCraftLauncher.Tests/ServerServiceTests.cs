using Microsoft.Extensions.Logging;
using Moq;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Infrastructure.Services;
using Xunit;

namespace ShoroCraftLauncher.Tests;

public class ServerServiceTests
{
    private static readonly ILogger<ServerService> Logger = Mock.Of<ILogger<ServerService>>();
    private static readonly Mock<ILogService> LogService = new(MockBehavior.Loose);

    private static ServerService CreateService(
        Mock<IServerRepository>? repository = null,
        Mock<IMinecraftService>? minecraftService = null,
        Mock<IJavaService>? javaService = null,
        HttpClient? httpClient = null)
    {
        return new ServerService(
            repository?.Object ?? Mock.Of<IServerRepository>(),
            minecraftService?.Object ?? Mock.Of<IMinecraftService>(),
            javaService?.Object ?? Mock.Of<IJavaService>(),
            httpClient ?? new HttpClient(),
            Logger,
            LogService.Object);
    }

    [Fact]
    public async Task CreateServerAsync_CreatesDirectoryEulaAndServerProperties()
    {
        using var dataRootScope = TestPaths.UseLauncherDataRoot("ShoroCraftServerCreate", out var dataRoot);
        var expectedDir = Path.Combine(dataRoot, "servers", "MyServer");

        var mockRepo = new Mock<IServerRepository>(MockBehavior.Strict);
        mockRepo.Setup(x => x.CreateAsync(It.IsAny<MinecraftServer>())).ReturnsAsync((MinecraftServer s) => { s.Id = 1; return s.Id; });

        try
        {
            var service = CreateService(repository: mockRepo);
            var server = await service.CreateServerAsync("MyServer", ServerType.Vanilla, "1.21", 4096, "world");

            Assert.Equal(1, server.Id);
            Assert.Equal("MyServer", server.Name);
            Assert.Equal(ServerType.Vanilla, server.Type);
            Assert.Equal("1.21", server.MinecraftVersion);
            Assert.Equal(4096, server.MaxRamMB);
            Assert.Equal(expectedDir, server.DirectoryPath);

            var eulaPath = Path.Combine(expectedDir, "eula.txt");
            var propsPath = Path.Combine(expectedDir, "server.properties");
            Assert.True(File.Exists(eulaPath));
            Assert.True(File.Exists(propsPath));

            var eula = await File.ReadAllTextAsync(eulaPath);
            Assert.Contains("eula=true", eula);

            var props = await File.ReadAllTextAsync(propsPath);
            Assert.Contains("server-port=25565", props);
            Assert.Contains("level-name=world", props);
            Assert.Contains("pause-when-empty-seconds=0", props);
        }
        finally
        {
            if (Directory.Exists(expectedDir))
                Directory.Delete(expectedDir, true);
        }
    }

    [Fact]
    public async Task DeleteServerAsync_DeletesDirectoryAndRepositoryEntry()
    {
        var tempDir = TestPaths.CreateTempDir("ShoroCraftServerDelete");

        var server = new MinecraftServer
        {
            Id = 7,
            Name = "TempServer",
            Type = ServerType.Paper,
            MinecraftVersion = "1.21",
            DirectoryPath = tempDir,
            MaxRamMB = 2048
        };

        var deletedId = 0;
        var mockRepo = new Mock<IServerRepository>(MockBehavior.Strict);
        mockRepo.Setup(x => x.DeleteAsync(It.IsAny<int>())).Returns<int>(id =>
        {
            deletedId = id;
            return Task.CompletedTask;
        });

        try
        {
            var service = CreateService(repository: mockRepo);
            await service.DeleteServerAsync(server);

            Assert.Equal(7, deletedId);
            Assert.False(Directory.Exists(tempDir));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task GetAvailableVanillaVersionsAsync_ReturnsOnlyReleaseVersions()
    {
        var mockMinecraft = new Mock<IMinecraftService>(MockBehavior.Strict);
        mockMinecraft.Setup(x => x.FetchAvailableVersionsAsync()).ReturnsAsync(new List<GameVersion>
        {
            new() { VersionId = "1.21.4", VersionType = "release" },
            new() { VersionId = "24w45a", VersionType = "snapshot" },
            new() { VersionId = "1.20.6", VersionType = "release" },
            new() { VersionId = "1.21.5", VersionType = "release" }
        });

        var service = CreateService(minecraftService: mockMinecraft);
        var versions = await service.GetAvailableVanillaVersionsAsync();

        Assert.Equal(new[] { "1.21.4", "1.20.6", "1.21.5" }, versions);
        Assert.DoesNotContain("24w45a", versions);
    }

    [Fact]
    public async Task GetAvailablePaperVersionsAsync_ParsesManifest()
    {
        var json = """
            {
              "project": { "id": "paper", "name": "Paper" },
              "versions": {
                "1.21": ["1.21.5", "1.21.4", "1.21.3"],
                "1.20": ["1.20.6", "1.20.4"]
              }
            }
            """;

        var handler = new StubHttpMessageHandler(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(handler);

        var service = CreateService(httpClient: httpClient);
        var versions = await service.GetAvailablePaperVersionsAsync();

        Assert.Equal(new[] { "1.21.5", "1.21.4", "1.21.3", "1.20.6", "1.20.4" }, versions);
    }

    [Fact]
    public async Task GetAvailablePaperVersionsAsync_OnError_ReturnsEmpty()
    {
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));
        var httpClient = new HttpClient(handler);

        var service = CreateService(httpClient: httpClient);
        var versions = await service.GetAvailablePaperVersionsAsync();

        Assert.Empty(versions);
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public StubHttpMessageHandler(HttpResponseMessage response) => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            => Task.FromResult(_response);
    }
}
