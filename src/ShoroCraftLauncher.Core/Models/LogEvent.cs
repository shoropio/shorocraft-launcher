namespace ShoroCraftLauncher.Core.Models;

public enum LauncherLogLevel
{
    Trace,
    Debug,
    Info,
    Warning,
    Error,
    Critical
}

public sealed record LogEvent(
    DateTimeOffset Timestamp,
    LauncherLogLevel Level,
    string SessionId,
    string? OperationId,
    string Module,
    string EventName,
    string Message,
    IReadOnlyDictionary<string, object?> Data);

public sealed record DiagnosticExportOptions(
    bool IncludeLauncherLogs = true,
    bool IncludeMinecraftLogs = true,
    bool IncludeDatabaseInfo = true,
    bool IncludeMinecraftListing = true);
