using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using ShoroCraftLauncher.Infrastructure.Downloading;
using Xunit;

namespace ShoroCraftLauncher.Tests;

public class ResumableDownloadServiceTests
{
    private static byte[] CreateContent(int size)
    {
        var bytes = new byte[size];
        for (int i = 0; i < size; i++) bytes[i] = (byte)(i % 251);
        return bytes;
    }

    private static string TempDir()
    {
        return TestPaths.CreateTempDir("ShoroResumableTests");
    }

    private static ResumableDownloadService CreateService(ScriptedHttpMessageHandler handler)
        => new(new HttpClient(handler), idleTimeout: TimeSpan.FromSeconds(30), maxAttempts: 3, retryDelay: TimeSpan.Zero);

    private static HttpResponseMessage PartialResponse(byte[] content, long from)
    {
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent(content.Skip((int)from).ToArray())
        };
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, content.Length - 1, content.Length);
        return response;
    }

    [Fact]
    public async Task DownloadAsync_FreshDownload_WritesCompleteFile()
    {
        var content = CreateContent(100_000);
        var handler = new ScriptedHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content)
        });
        var dest = Path.Combine(TempDir(), "file.bin");

        await CreateService(handler).DownloadAsync("https://example.com/file.bin", dest);

        Assert.Equal(content, File.ReadAllBytes(dest));
        Assert.Equal(1, handler.RequestCount);
        Assert.Null(handler.LastRange);
    }

    [Fact]
    public async Task DownloadAsync_ResumesFromExistingPartFile()
    {
        var content = CreateContent(100_000);
        var partSize = 40_000L;
        var handler = new ScriptedHttpMessageHandler(request =>
        {
            var from = request.Headers.Range?.Ranges.FirstOrDefault()?.From ?? 0;
            return PartialResponse(content, from);
        });
        var dest = Path.Combine(TempDir(), "file.bin");
        await File.WriteAllBytesAsync(dest + ".part", content.Take((int)partSize).ToArray());

        await CreateService(handler).DownloadAsync("https://example.com/file.bin", dest);

        Assert.Equal(content, File.ReadAllBytes(dest));
        Assert.Equal(1, handler.RequestCount);
        Assert.NotNull(handler.LastRange);
        Assert.Equal(partSize, handler.LastRange!.Ranges.Single().From);
    }

    [Fact]
    public async Task DownloadAsync_ServerWithoutRangeSupport_RestartsFromZero()
    {
        var content = CreateContent(50_000);
        var handler = new ScriptedHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content)
        });
        var dest = Path.Combine(TempDir(), "file.bin");
        await File.WriteAllBytesAsync(dest + ".part", CreateContent(30_000));

        await CreateService(handler).DownloadAsync("https://example.com/file.bin", dest);

        Assert.Equal(content, File.ReadAllBytes(dest));
        Assert.Equal(1, handler.RequestCount);
        Assert.NotNull(handler.LastRange);
    }

    [Fact]
    public async Task DownloadAsync_RangeNotSatisfiable_RestartsFromScratch()
    {
        var content = CreateContent(20_000);
        var handler = new ScriptedHttpMessageHandler(request =>
        {
            if (request.Headers.Range != null)
                return new HttpResponseMessage(HttpStatusCode.RequestedRangeNotSatisfiable);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
        });
        var dest = Path.Combine(TempDir(), "file.bin");
        await File.WriteAllBytesAsync(dest + ".part", CreateContent(5_000));

        await CreateService(handler).DownloadAsync("https://example.com/file.bin", dest);

        Assert.Equal(content, File.ReadAllBytes(dest));
        Assert.Equal(2, handler.RequestCount);
        Assert.False(File.Exists(dest + ".part"));
    }

    [Fact]
    public async Task DownloadAsync_RetriesAfterTransientFailure()
    {
        var content = CreateContent(20_000);
        var attempts = 0;
        var handler = new ScriptedHttpMessageHandler(_ =>
        {
            attempts++;
            if (attempts == 1)
                throw new HttpRequestException("Connection reset");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };
        });
        var dest = Path.Combine(TempDir(), "file.bin");

        await CreateService(handler).DownloadAsync("https://example.com/file.bin", dest);

        Assert.Equal(content, File.ReadAllBytes(dest));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task DownloadAsync_AllAttemptsFail_ThrowsDownloadException()
    {
        var handler = new ScriptedHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var dest = Path.Combine(TempDir(), "file.bin");

        var service = CreateService(handler);
        var ex = await Assert.ThrowsAsync<DownloadException>(() =>
            service.DownloadAsync("https://example.com/file.bin", dest));

        Assert.Contains("3 intentos", ex.Message);
        Assert.Equal(3, handler.RequestCount);
        Assert.False(File.Exists(dest));
    }

    [Fact]
    public async Task DownloadAsync_ValidSha1_DownloadsFile()
    {
        var content = CreateContent(10_000);
        var sha1 = Convert.ToHexString(SHA1.HashData(content)).ToLowerInvariant();
        var handler = new ScriptedHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content)
        });
        var dest = Path.Combine(TempDir(), "file.bin");

        await CreateService(handler).DownloadAsync("https://example.com/file.bin", dest, sha1, content.Length);

        Assert.Equal(content, File.ReadAllBytes(dest));
    }

    [Fact]
    public async Task DownloadAsync_HashMismatch_ThrowsAndKeepsPartFile()
    {
        var content = CreateContent(10_000);
        var handler = new ScriptedHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content)
        });
        var dest = Path.Combine(TempDir(), "file.bin");

        var service = CreateService(handler);
        await Assert.ThrowsAsync<DownloadException>(() =>
            service.DownloadAsync("https://example.com/file.bin", dest, "wrongsha1", 0));

        Assert.True(File.Exists(dest + ".part"));
        Assert.False(File.Exists(dest));
    }

    [Fact]
    public async Task DownloadAsync_SizeMismatch_ThrowsAndKeepsPartFile()
    {
        var content = CreateContent(10_000);
        var handler = new ScriptedHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content)
        });
        var dest = Path.Combine(TempDir(), "file.bin");

        var service = CreateService(handler);
        await Assert.ThrowsAsync<DownloadException>(() =>
            service.DownloadAsync("https://example.com/file.bin", dest, null, content.Length + 5));

        Assert.True(File.Exists(dest + ".part"));
        Assert.False(File.Exists(dest));
    }

    private sealed class ScriptedHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public ScriptedHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        public int RequestCount { get; private set; }

        public RangeHeaderValue? LastRange { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            LastRange = request.Headers.Range;
            return Task.FromResult(_responder(request));
        }
    }
}
