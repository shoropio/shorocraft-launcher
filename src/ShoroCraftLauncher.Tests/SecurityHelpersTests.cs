using Microsoft.Extensions.Logging;
using Moq;
using ShoroCraftLauncher.Infrastructure.Authentication;
using ShoroCraftLauncher.Infrastructure.Downloading;
using Xunit;

namespace ShoroCraftLauncher.Tests;

public class SecurityHelpersTests
{
    [Theory]
    [InlineData(@"..\..\..\evil.dll")]
    [InlineData("mods/subdir/jei.jar")]
    [InlineData("..\\..\\x.jar")]
    public void SafeFileName_PathComponents_Throws(string input)
    {
        Assert.Throws<Exception>(() => DownloadPathGuard.SafeFileName(input));
    }

    [Fact]
    public void SafeFileName_NormalName_Unchanged()
    {
        Assert.Equal("jei-1.0.jar", DownloadPathGuard.SafeFileName("  jei-1.0.jar  "));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("..")]
    [InlineData(".")]
    public void SafeFileName_EmptyOrDotOnly_Throws(string input)
    {
        Assert.Throws<Exception>(() => DownloadPathGuard.SafeFileName(input));
    }

    [Theory]
    [InlineData("a|b.jar")]
    [InlineData("a:b.jar")]
    [InlineData("a*b.jar")]
    [InlineData("a?b.jar")]
    public void SafeFileName_InvalidChars_Throws(string input)
    {
        Assert.Throws<Exception>(() => DownloadPathGuard.SafeFileName(input));
    }

    [Fact]
    public async Task ApiRateLimiter_AllowsUpToMaxRequestsImmediately()
    {
        var limiter = new ApiRateLimiter(3, TimeSpan.FromSeconds(5));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await limiter.WaitAsync();
        await limiter.WaitAsync();
        await limiter.WaitAsync();
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 1000, $"Las 3 primeras peticiones no debieron esperar; tardaron {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task ApiRateLimiter_BlocksRequestBeyondMaxUntilWindowExpires()
    {
        var limiter = new ApiRateLimiter(2, TimeSpan.FromMilliseconds(500));

        await limiter.WaitAsync();
        await limiter.WaitAsync();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await limiter.WaitAsync();
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds >= 300, $"La petición extra debió esperar a que expirara la ventana; solo esperó {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task ApiRateLimiter_WindowExpiry_ReleasesSlots()
    {
        var limiter = new ApiRateLimiter(1, TimeSpan.FromMilliseconds(200));

        await limiter.WaitAsync();
        await Task.Delay(250);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await limiter.WaitAsync();
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 150, $"Tras expirar la ventana la petición debió ser inmediata; tardó {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void ApiRateLimiter_ZeroMaxRequests_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApiRateLimiter(0, TimeSpan.FromSeconds(1)));
    }
}

public class AuthenticationServiceTests
{
    [Fact]
    public void Constructor_WithLogger_DoesNotThrow()
    {
        var service = new AuthenticationService(Mock.Of<ILogger<AuthenticationService>>(), new HttpClient());
        Assert.NotNull(service);
    }
}
