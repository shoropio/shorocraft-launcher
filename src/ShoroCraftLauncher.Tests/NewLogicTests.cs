using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Moq;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Infrastructure.Authentication;
using ShoroCraftLauncher.Infrastructure.Services;
using Xunit;

namespace ShoroCraftLauncher.Tests;

public class NewLogicTests
{
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }

    private static AuthenticationService CreateAuthService(HttpMessageHandler handler)
    {
        return new AuthenticationService(Mock.Of<ILogger<AuthenticationService>>(), new HttpClient(handler));
    }

    [Fact]
    public async Task ValidateTokenAsync_ReturnsTrue_OnSuccessStatusCode()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateAuthService(handler);

        Assert.True(await service.ValidateTokenAsync("valid-token"));
    }

    [Fact]
    public async Task ValidateTokenAsync_ReturnsFalse_OnUnauthorized()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var service = CreateAuthService(handler);

        Assert.False(await service.ValidateTokenAsync("expired-token"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("offline")]
    [InlineData(null)]
    public async Task ValidateTokenAsync_ReturnsFalse_OnEmptyOrOffline(string? token)
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateAuthService(handler);

        Assert.False(await service.ValidateTokenAsync(token!));
    }

    [Fact]
    public async Task ValidateAndRefreshAsync_ReturnsSameInstance_WhenTokenValid()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateAuthService(handler);

        var current = new AuthResult { Success = true, AccessToken = "token", Username = "user", Uuid = "uuid" };
        var result = await service.ValidateAndRefreshAsync(current);

        Assert.True(ReferenceEquals(current, result));
    }

    [Fact]
    public async Task ServerService_OnlineMode_UpdatesServerPropertiesFile()
    {
        using var dataRootScope = TestPaths.UseLauncherDataRoot("ShoroCraftOnlineMode", out var dataRoot);
        var expectedDir = Path.Combine(dataRoot, "servers", "MyServer");

        var mockRepo = new Mock<IServerRepository>(MockBehavior.Strict);
        mockRepo.Setup(x => x.CreateAsync(It.IsAny<MinecraftServer>())).ReturnsAsync((MinecraftServer s) => { s.Id = 1; return s.Id; });

        var service = CreateServerService(mockRepo);
        var server = await service.CreateServerAsync("MyServer", ServerType.Vanilla, "1.21", 4096, "world");

        await service.SetOnlineModeAsync(server, false);
        Assert.False(await service.GetOnlineModeAsync(server));

        var props = await File.ReadAllTextAsync(Path.Combine(expectedDir, "server.properties"));
        Assert.Contains("online-mode=false", props);

        await service.SetOnlineModeAsync(server, true);
        Assert.True(await service.GetOnlineModeAsync(server));
        Assert.Contains("online-mode=true", await File.ReadAllTextAsync(Path.Combine(expectedDir, "server.properties")));

        if (Directory.Exists(expectedDir))
            Directory.Delete(expectedDir, true);
    }

    [Fact]
    public async Task ServerService_SaveAndGetServerProperties_RoundTrips()
    {
        using var dataRootScope = TestPaths.UseLauncherDataRoot("ShoroCraftProps", out var dataRoot);
        var expectedDir = Path.Combine(dataRoot, "servers", "MyServer");

        var mockRepo = new Mock<IServerRepository>(MockBehavior.Strict);
        mockRepo.Setup(x => x.CreateAsync(It.IsAny<MinecraftServer>())).ReturnsAsync((MinecraftServer s) => { s.Id = 1; return s.Id; });

        var service = CreateServerService(mockRepo);
        var server = await service.CreateServerAsync("MyServer", ServerType.Vanilla, "1.21", 4096, "world");

        var custom = "online-mode=false\nmotd=Test\nlevel-name=world";
        await service.SaveServerPropertiesAsync(server, custom);

        var read = await service.GetServerPropertiesAsync(server);
        Assert.Equal(custom, read);

        if (Directory.Exists(expectedDir))
            Directory.Delete(expectedDir, true);
    }

    private static ServerService CreateServerService(Mock<IServerRepository> repository)
    {
        return new ServerService(
            repository.Object,
            Mock.Of<IMinecraftService>(),
            Mock.Of<IJavaService>(),
            new HttpClient(),
            Mock.Of<ILogger<ServerService>>());
    }
}
