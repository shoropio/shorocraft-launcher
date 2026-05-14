using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Core.Interfaces;

public interface ILogService
{
    string SessionId { get; }
    string SessionDirectory { get; }
    IReadOnlyList<LogEvent> RecentEvents { get; }

    event EventHandler<LogEvent>? LogReceived;

    IDisposable BeginOperation(string module, string operationName, object? context = null);

    void Trace(string module, string eventName, string message, object? data = null);
    void Debug(string module, string eventName, string message, object? data = null);
    void Info(string module, string eventName, string message, object? data = null);
    void Warning(string module, string eventName, string message, object? data = null);
    void Error(string module, string eventName, string message, Exception? exception = null, object? data = null);
    void Critical(string module, string eventName, string message, Exception? exception = null, object? data = null);

    void MinecraftStdout(string line);
    void MinecraftStderr(string line);

    Task FlushAsync(CancellationToken cancellationToken = default);
    Task<string> ExportDiagnosticsZipAsync(DiagnosticExportOptions options, CancellationToken cancellationToken = default);
}
