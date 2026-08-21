namespace ShoroCraftLauncher.Infrastructure.Downloading;

public sealed class ApiRateLimiter
{
    private readonly object _lock = new();
    private readonly Queue<DateTimeOffset> _timestamps = new();
    private readonly int _maxRequests;
    private readonly TimeSpan _window;

    public ApiRateLimiter(int maxRequests, TimeSpan window)
    {
        if (maxRequests < 1) throw new ArgumentOutOfRangeException(nameof(maxRequests));
        _maxRequests = maxRequests;
        _window = window;
    }

    public async Task WaitAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            TimeSpan delay;
            lock (_lock)
            {
                var now = DateTimeOffset.UtcNow;
                while (_timestamps.Count > 0 && now - _timestamps.Peek() >= _window)
                    _timestamps.Dequeue();

                if (_timestamps.Count < _maxRequests)
                {
                    _timestamps.Enqueue(now);
                    return;
                }

                delay = _timestamps.Peek() + _window - now;
                if (delay < TimeSpan.Zero) delay = TimeSpan.FromMilliseconds(50);
            }

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }
}

public static class ModrinthApiRateLimiter
{
    private static readonly Lazy<ApiRateLimiter> Instance = new(() => new ApiRateLimiter(240, TimeSpan.FromMinutes(1)));
    public static Task WaitAsync(CancellationToken cancellationToken = default) => Instance.Value.WaitAsync(cancellationToken);
}

public static class CurseForgeApiRateLimiter
{
    private static readonly Lazy<ApiRateLimiter> Instance = new(() => new ApiRateLimiter(120, TimeSpan.FromMinutes(1)));
    public static Task WaitAsync(CancellationToken cancellationToken = default) => Instance.Value.WaitAsync(cancellationToken);
}
