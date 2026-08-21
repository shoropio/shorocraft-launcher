using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace ShoroCraftLauncher.Infrastructure.Downloading;

public interface IResumableDownloadService
{
    Task DownloadAsync(string url, string destinationPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default);

    Task DownloadAsync(string url, string destinationPath, string? expectedSha1, long expectedSize, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

public sealed class ResumableDownloadService : IResumableDownloadService
{
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _idleTimeout;
    private readonly TimeSpan _retryDelay;
    private readonly int _maxAttempts;
    private const int BufferSize = 81920;

    public ResumableDownloadService(HttpClient httpClient, TimeSpan? idleTimeout = null, int maxAttempts = 5, TimeSpan? retryDelay = null)
    {
        _httpClient = httpClient;
        _idleTimeout = idleTimeout ?? TimeSpan.FromSeconds(60);
        _maxAttempts = maxAttempts;
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(1);
    }

    public async Task DownloadAsync(string url, string destinationPath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        => await DownloadAsync(url, destinationPath, null, 0, progress, cancellationToken).ConfigureAwait(false);

    public async Task DownloadAsync(string url, string destinationPath, string? expectedSha1, long expectedSize,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var partPath = destinationPath + ".part";
        var completed = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

        Exception? lastError = null;

        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (attempt > 1)
            {
                var backoff = TimeSpan.FromSeconds(Math.Min(30, _retryDelay.TotalSeconds * attempt));
                try { await Task.Delay(backoff, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            }

            try
            {
                completed = await DownloadCoreAsync(url, partPath, completed, progress, cancellationToken).ConfigureAwait(false);
                Finalize(partPath, destinationPath, expectedSha1, expectedSize);
                return;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = null;
            }
            catch (Exception ex) when (ex is DownloadException or HttpRequestException or IOException or UnauthorizedAccessException)
            {
                lastError = ex;
            }
        }

        throw new DownloadException(
            $"No se pudo completar la descarga tras {_maxAttempts} intentos: {url}" +
            (lastError is null ? string.Empty : $" ({lastError.Message})"),
            lastError);
    }

    private async Task<long> DownloadCoreAsync(string url, string partPath, long completed, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (request.Headers.UserAgent.Count == 0)
            request.Headers.UserAgent.ParseAdd("ShoroCraftLauncher/1.0");
        if (completed > 0)
            request.Headers.Range = new RangeHeaderValue(completed, null);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            File.Delete(partPath);
            return await DownloadCoreAsync(url, partPath, 0, progress, cancellationToken).ConfigureAwait(false);
        }

        if (response.StatusCode == HttpStatusCode.OK)
        {
            File.Delete(partPath);
            completed = 0;
        }
        else if (response.StatusCode != HttpStatusCode.PartialContent)
        {
            response.EnsureSuccessStatusCode();
        }

        var totalBytes = GetTotalBytes(response, completed);
        using var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

        await using (var fileStream = new FileStream(partPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
        {
            fileStream.Seek(completed, SeekOrigin.Begin);

            var buffer = new byte[BufferSize];
            var written = completed;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await ReadWithIdleTimeoutAsync(contentStream, buffer, cancellationToken).ConfigureAwait(false);
                if (read <= 0) break;

                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                written += read;
                if (totalBytes > 0)
                    progress?.Report(Math.Min(written, totalBytes) / (double)totalBytes * 100);
            }

            if (totalBytes > 0 && written != totalBytes)
                throw new DownloadException($"Tamaño esperado {totalBytes} bytes, se descargaron {written}.");

            await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            return written;
        }
    }

    private async Task<int> ReadWithIdleTimeoutAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_idleTimeout);

        try
        {
            return await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DownloadException($"La descarga quedó sin actividad durante {_idleTimeout.TotalSeconds:0} segundos.");
        }
    }

    private static long GetTotalBytes(HttpResponseMessage response, long completed)
    {
        if (response.Content.Headers.ContentRange is { HasRange: true, Length: { } total })
            return total;

        if (response.Content.Headers.ContentLength is { } length)
            return response.StatusCode == HttpStatusCode.PartialContent ? completed + length : length;

        return -1;
    }

    private static void Finalize(string partPath, string destinationPath, string? expectedSha1, long expectedSize)
    {
        var size = new FileInfo(partPath).Length;
        if (expectedSize > 0 && size != expectedSize)
            throw new DownloadException($"Tamaño {size} no coincide con el esperado {expectedSize}.");

        if (!string.IsNullOrEmpty(expectedSha1))
        {
            using var stream = File.OpenRead(partPath);
            var hash = Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant();
            if (!string.Equals(hash, expectedSha1.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
                throw new DownloadException("El hash SHA1 del archivo no coincide.");
        }

        File.Move(partPath, destinationPath, true);
    }
}

public class DownloadException : Exception
{
    public DownloadException(string message) : base(message) { }

    public DownloadException(string message, Exception? innerException) : base(message, innerException) { }
}
