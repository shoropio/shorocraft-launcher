using System.Net;
using System.Net.Sockets;

namespace ShoroCraftLauncher.Infrastructure.Downloading;

public sealed class RetryDelegatingHandler : DelegatingHandler
{
    private readonly int _maxRetries;
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;

    public RetryDelegatingHandler(int maxRetries = 3, TimeSpan? baseDelay = null)
    {
        _maxRetries = Math.Max(1, maxRetries);
        _baseDelay = baseDelay ?? TimeSpan.FromMilliseconds(500);
        _maxDelay = TimeSpan.FromSeconds(10);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage? response;
            try
            {
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < _maxRetries && !cancellationToken.IsCancellationRequested && IsTransient(ex))
            {
                await Delay(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (!ShouldRetry(response.StatusCode) || attempt >= _maxRetries)
                return response;

            response.Dispose();
            await Delay(attempt, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsTransient(Exception ex)
        => ex is HttpRequestException
           || ex is IOException
           || ex is SocketException
           || ex is TaskCanceledException
           || ex is TimeoutException;

    private static bool ShouldRetry(HttpStatusCode statusCode)
        => statusCode == HttpStatusCode.RequestTimeout
           || statusCode == HttpStatusCode.TooManyRequests
           || (int)statusCode >= 500;

    private async Task Delay(int attempt, CancellationToken cancellationToken)
    {
        var backoff = TimeSpan.FromMilliseconds(
            Math.Min(_maxDelay.TotalMilliseconds, _baseDelay.TotalMilliseconds * Math.Pow(2, attempt)));
        await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
    }
}
