using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Moq;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Infrastructure.Services;
using Xunit;

namespace ShoroCraftLauncher.Tests;

public class ServerServiceStartStopTests
{
    private static readonly ILogger<ServerService> Logger = Mock.Of<ILogger<ServerService>>();
    private static readonly Mock<ILogService> LogService = new(MockBehavior.Loose);

    private static ServerService CreateService(
        Mock<IServerRepository>? repository = null,
        Mock<IMinecraftService>? minecraft = null,
        Mock<IJavaService>? javaService = null,
        HttpClient? httpClient = null)
    {
        return new ServerService(
            repository?.Object ?? Mock.Of<IServerRepository>(),
            minecraft?.Object ?? Mock.Of<IMinecraftService>(),
            javaService?.Object ?? Mock.Of<IJavaService>(),
            httpClient ?? new HttpClient(),
            Logger,
            LogService.Object);
    }

    [Fact]
    public async Task StopAsync_WhenNotRunning_IsNoOp()
    {
        var mockRepo = new Mock<IServerRepository>(MockBehavior.Strict);
        var service = CreateService(repository: mockRepo);

        var server = new MinecraftServer { Id = 99, Name = "GhostServer" };

        await service.StopAsync(server);
    }

    [Fact]
    public async Task StopAllAsync_WhenNoServers_IsNoOp()
    {
        var mockRepo = new Mock<IServerRepository>(MockBehavior.Strict);
        mockRepo.Setup(x => x.GetAllAsync()).ReturnsAsync(new List<MinecraftServer>());

        var service = CreateService(repository: mockRepo);
        await service.LoadAsync();

        await service.StopAllAsync();
    }

    [Fact]
    public async Task GetLogHistory_ReturnsEmptyForNewServer()
    {
        var service = CreateService();
        var server = new MinecraftServer { Id = 1, Name = "Test" };

        var history = service.GetLogHistory(server);

        Assert.Empty(history);
    }

    [Fact]
    public async Task IsRunning_ReturnsFalseForUnknownServer()
    {
        var service = CreateService();
        var server = new MinecraftServer { Id = 1, Name = "Test" };

        Assert.False(service.IsRunning(server));
    }

    [Fact]
    public async Task CreateServerAsync_OnlineModeCanBeDisabled()
    {
        var mockRepo = new Mock<IServerRepository>(MockBehavior.Strict);
        mockRepo.Setup(x => x.CreateAsync(It.IsAny<MinecraftServer>())).ReturnsAsync((MinecraftServer s) => { s.Id = 1; return s.Id; });

        var service = CreateService(repository: mockRepo);
        var server = await service.CreateServerAsync("ModeOffSrv", ServerType.Vanilla, "1.21", 4096, "world", onlineMode: false);

        Assert.Equal(1, server.Id);

        var propsPath = Path.Combine(server.DirectoryPath, "server.properties");
        var props = await File.ReadAllTextAsync(propsPath);
        Assert.Contains("online-mode=false", props);

        if (Directory.Exists(server.DirectoryPath)) Directory.Delete(server.DirectoryPath, true);
    }

    [Fact]
    public async Task GetServerPropertiesAsync_ReturnsNullForMissingFile()
    {
        var tempDir = TestPaths.CreateTempDir("ShoroProps");
        var mockRepo = new Mock<IServerRepository>(MockBehavior.Strict);
        var service = CreateService(repository: mockRepo);

        var server = new MinecraftServer { Id = 1, Name = "Test", DirectoryPath = tempDir };

        var props = await service.GetServerPropertiesAsync(server);

        Assert.Null(props);

        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
    }
}
