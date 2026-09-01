using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Moq;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Infrastructure.Authentication;
using Xunit;

namespace ShoroCraftLauncher.Tests;

public class AuthenticationServiceTests
{
    private static AuthenticationService CreateAuthService(HttpMessageHandler handler)
        => new(Mock.Of<ILogger<AuthenticationService>>(), new HttpClient(handler));

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    [Fact]
    public async Task AuthenticateOfflineAsync_WithValidUsername_ReturnsOfflineResult()
    {
        var service = CreateAuthService(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        var result = service.AuthenticateOfflineAsync("Player");

        Assert.True(result.Success);
        Assert.True(result.IsOffline);
        Assert.Equal("offline", result.AccessToken);
        Assert.Equal("Player", result.Username);
        Assert.NotEmpty(result.Uuid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task AuthenticateOfflineAsync_WithEmptyUsername_ReturnsFailure(string? username)
    {
        var service = CreateAuthService(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        var result = service.AuthenticateOfflineAsync(username!);

        Assert.False(result.Success);
        Assert.False(result.IsOffline);
    }

    [Fact]
    public async Task AuthenticateOfflineAsync_SameUsernameReturnsSameUuid()
    {
        var service = CreateAuthService(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        var r1 = service.AuthenticateOfflineAsync("TestPlayer");
        var r2 = service.AuthenticateOfflineAsync("TestPlayer");

        Assert.Equal(r1.Uuid, r2.Uuid);
    }

    [Fact]
    public async Task AuthenticateOfflineAsync_DifferentUsernamesReturnDifferentUuid()
    {
        var service = CreateAuthService(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        var r1 = service.AuthenticateOfflineAsync("Player1");
        var r2 = service.AuthenticateOfflineAsync("Player2");

        Assert.NotEqual(r1.Uuid, r2.Uuid);
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
    public async Task ValidateAndRefreshAsync_ReturnsCurrent_WhenTokenValid()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateAuthService(handler);

        var current = new AuthResult { Success = true, AccessToken = "token", Username = "user", Uuid = "uuid" };
        var result = await service.ValidateAndRefreshAsync(current);

        Assert.True(ReferenceEquals(current, result));
    }

    [Fact]
    public async Task ValidateAndRefreshAsync_ReturnsCurrent_WhenOffline()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateAuthService(handler);

        var current = new AuthResult { Success = true, IsOffline = true, AccessToken = "offline", Username = "user", Uuid = "uuid" };
        var result = await service.ValidateAndRefreshAsync(current);

        Assert.True(ReferenceEquals(current, result));
    }

    [Fact]
    public async Task ValidateAndRefreshAsync_ReturnsCurrent_WhenNull()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateAuthService(handler);

        var result = await service.ValidateAndRefreshAsync(null!);

        Assert.Null(result);
    }

    [Fact]
    public async Task LogoutAsync_DoesNotThrow()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = CreateAuthService(handler);

        await service.LogoutAsync();
    }
}
