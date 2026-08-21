using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Moq;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Infrastructure.Downloading;
using ShoroCraftLauncher.Infrastructure.Services;
using Xunit;

namespace ShoroCraftLauncher.Tests;

public class UpdaterServiceTests
{
    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_responder(request));
    }

    private static HttpResponseMessage JsonRelease(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static UpdaterService CreateService(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var httpClient = new HttpClient(new FakeHttpHandler(responder));
        return new UpdaterService(
            httpClient,
            Mock.Of<ILogger<UpdaterService>>(),
            Mock.Of<IResumableDownloadService>());
    }

    [Fact]
    public async Task CheckForUpdates_NewerVersion_ReturnsUpdate()
    {
        var service = CreateService(_ => JsonRelease(
            """{"tag_name":"v1.7.0","assets":[{"name":"ShoroCraftLauncher_Setup.exe","browser_download_url":"https://example.com/setup.exe","digest":"sha256:abc123"}]}"""));

        var (isUpdate, latestVersion, downloadUrl, sha256) = await service.CheckForUpdatesAsync("1.6.4");

        Assert.True(isUpdate);
        Assert.Equal("v1.7.0", latestVersion);
        Assert.Equal("https://example.com/setup.exe", downloadUrl);
        Assert.Equal("sha256:abc123", sha256);
    }

    [Fact]
    public async Task CheckForUpdates_SameVersion_ReturnsNoUpdate()
    {
        var service = CreateService(_ => JsonRelease(
            """{"tag_name":"v1.6.4","assets":[{"name":"Setup.exe","browser_download_url":"https://example.com/setup.exe"}]}"""));

        var (isUpdate, latestVersion, downloadUrl, sha256) = await service.CheckForUpdatesAsync("v1.6.4");

        Assert.False(isUpdate);
        Assert.Null(latestVersion);
        Assert.Null(downloadUrl);
        Assert.Null(sha256);
    }

    [Fact]
    public async Task CheckForUpdates_OlderRemoteVersion_ReturnsNoUpdate()
    {
        var service = CreateService(_ => JsonRelease(
            """{"tag_name":"v1.0.0","assets":[]}"""));

        var (isUpdate, _, _, _) = await service.CheckForUpdatesAsync("1.6.4");

        Assert.False(isUpdate);
    }

    [Fact]
    public async Task CheckForUpdates_HttpError_ReturnsNoUpdate()
    {
        var service = CreateService(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var (isUpdate, _, _, _) = await service.CheckForUpdatesAsync("1.6.4");

        Assert.False(isUpdate);
    }

    [Fact]
    public async Task CheckForUpdates_InvalidJson_ReturnsNoUpdate()
    {
        var service = CreateService(_ => JsonRelease("not-json"));

        var (isUpdate, _, _, _) = await service.CheckForUpdatesAsync("1.6.4");

        Assert.False(isUpdate);
    }
}
