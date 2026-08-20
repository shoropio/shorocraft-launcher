using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Collections;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Channels;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Infrastructure.Services;

public sealed class LogService : ILogService, IDisposable
{
    private readonly Channel<LogWrite> _queue = Channel.CreateUnbounded<LogWrite>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _writerTask;
    private readonly object _recentLock = new();
    private readonly List<LogEvent> _recentEvents = new();
    private readonly AsyncLocal<string?> _operationId = new();
    private readonly string _launcherLogPath;
    private readonly string _jsonLogPath;
    private readonly string _minecraftStdoutPath;
    private readonly string _minecraftStderrPath;

    public string SessionId { get; } = $"s_{DateTimeOffset.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"[..28];
    public string SessionDirectory { get; }

    public IReadOnlyList<LogEvent> RecentEvents
    {
        get
        {
            lock (_recentLock)
                return _recentEvents.ToList();
        }
    }

    public event EventHandler<LogEvent>? LogReceived;

    public LogService()
    {
        var logsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShoroCraftLauncher", "logs");
        SessionDirectory = Path.Combine(logsRoot, "sessions", SessionId);
        Directory.CreateDirectory(SessionDirectory);
        Directory.CreateDirectory(Path.Combine(SessionDirectory, "operations"));
        Directory.CreateDirectory(Path.Combine(logsRoot, "latest"));

        _launcherLogPath = Path.Combine(SessionDirectory, "launcher.log");
        _jsonLogPath = Path.Combine(SessionDirectory, "launcher.jsonl");
        _minecraftStdoutPath = Path.Combine(SessionDirectory, "minecraft_stdout.log");
        _minecraftStderrPath = Path.Combine(SessionDirectory, "minecraft_stderr.log");

        _writerTask = Task.Run(ProcessQueueAsync);
    }

    public IDisposable BeginOperation(string module, string operationName, object? context = null)
    {
        var previous = _operationId.Value;
        var next = $"op_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}"[..31];
        _operationId.Value = next;
        Info(module, $"{operationName}Started", $"{operationName} iniciado.", Merge(context, new Dictionary<string, object?> { ["operationId"] = next }));
        return new OperationScope(this, previous, module, operationName, next);
    }

    public void Trace(string module, string eventName, string message, object? data = null) =>
        Write(LauncherLogLevel.Trace, module, eventName, message, data: data);

    public void Debug(string module, string eventName, string message, object? data = null) =>
        Write(LauncherLogLevel.Debug, module, eventName, message, data: data);

    public void Info(string module, string eventName, string message, object? data = null) =>
        Write(LauncherLogLevel.Info, module, eventName, message, data: data);

    public void Warning(string module, string eventName, string message, object? data = null) =>
        Write(LauncherLogLevel.Warning, module, eventName, message, data: data);

    public void Error(string module, string eventName, string message, Exception? exception = null, object? data = null) =>
        Write(LauncherLogLevel.Error, module, eventName, message, exception, data);

    public void Critical(string module, string eventName, string message, Exception? exception = null, object? data = null) =>
        Write(LauncherLogLevel.Critical, module, eventName, message, exception, data);

    public void MinecraftStdout(string line)
    {
        _queue.Writer.TryWrite(new LogWrite(MinecraftPath: _minecraftStdoutPath, Text: Sanitize(line)));
        Write(LauncherLogLevel.Info, "Minecraft", "Stdout", line);
    }

    public void MinecraftStderr(string line)
    {
        _queue.Writer.TryWrite(new LogWrite(MinecraftPath: _minecraftStderrPath, Text: Sanitize(line)));
        Write(LauncherLogLevel.Error, "Minecraft", "Stderr", line);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        while (_queue.Reader.Count > 0 && !cancellationToken.IsCancellationRequested)
            await Task.Delay(25, cancellationToken);
    }

    public async Task<string> ExportDiagnosticsZipAsync(DiagnosticExportOptions options, CancellationToken cancellationToken = default)
    {
        await FlushAsync(cancellationToken);

        var exportDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShoroCraftLauncher", "diagnostics");
        Directory.CreateDirectory(exportDir);

        var zipPath = Path.Combine(exportDir, $"diagnostic_{SessionId}_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        AddText(zip, "diagnostic.json", JsonSerializer.Serialize(new
        {
            SessionId,
            CreatedAt = DateTimeOffset.Now,
            SessionDirectory,
            Machine = Environment.MachineName,
            OS = Environment.OSVersion.ToString(),
            Runtime = Environment.Version.ToString(),
            MinecraftDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft")
        }, JsonOptions(writeIndented: true)));

        if (options.IncludeLauncherLogs)
        {
            AddFileIfExists(zip, _launcherLogPath, "launcher.log");
            AddFileIfExists(zip, _jsonLogPath, "launcher.jsonl");
        }

        if (options.IncludeMinecraftLogs)
        {
            AddFileIfExists(zip, _minecraftStdoutPath, "minecraft_stdout.log");
            AddFileIfExists(zip, _minecraftStderrPath, "minecraft_stderr.log");
        }

        if (options.IncludeMinecraftListing)
            AddText(zip, "minecraft_listing.txt", BuildMinecraftListing());

        if (options.IncludeDatabaseInfo)
            AddText(zip, "database_info.txt", BuildDatabaseInfo());

        Info("Diagnostics", "Exported", "Diagnóstico exportado.", new { zipPath });
        return zipPath;
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        _cts.CancelAfter(TimeSpan.FromSeconds(2));
        try { _writerTask.Wait(TimeSpan.FromSeconds(3)); } catch { }
        _cts.Dispose();
    }

    private void Write(LauncherLogLevel level, string module, string eventName, string message, Exception? exception = null, object? data = null)
    {
        var merged = Merge(data, exception is null ? null : new Dictionary<string, object?>
        {
            ["exceptionType"] = exception.GetType().FullName,
            ["exceptionMessage"] = Sanitize(exception.Message)
        });

        var logEvent = new LogEvent(
            DateTimeOffset.Now,
            level,
            SessionId,
            _operationId.Value,
            module,
            eventName,
            Sanitize(message),
            merged);

        lock (_recentLock)
        {
            _recentEvents.Add(logEvent);
            if (_recentEvents.Count > 3000)
                _recentEvents.RemoveRange(0, _recentEvents.Count - 3000);
        }

        LogReceived?.Invoke(this, logEvent);
        _queue.Writer.TryWrite(new LogWrite(Event: logEvent, Exception: exception));
    }

    private async Task ProcessQueueAsync()
    {
        await foreach (var write in _queue.Reader.ReadAllAsync(_cts.Token))
        {
            try
            {
                if (write.Event != null)
                {
                    await File.AppendAllTextAsync(_launcherLogPath, FormatHuman(write.Event, write.Exception) + Environment.NewLine, _cts.Token);
                    await File.AppendAllTextAsync(_jsonLogPath, JsonSerializer.Serialize(write.Event, JsonOptions(writeIndented: false)) + Environment.NewLine, _cts.Token);
                }

                if (write.MinecraftPath != null && write.Text != null)
                    await File.AppendAllTextAsync(write.MinecraftPath, write.Text + Environment.NewLine, _cts.Token);
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine("Failed to write ShoroCraft log entry.");
            }
        }
    }

    private static string FormatHuman(LogEvent e, Exception? exception)
    {
        var data = e.Data.Count == 0
            ? ""
            : " " + string.Join(" ", e.Data.Select(kv => $"{kv.Key}={Sanitize(Convert.ToString(kv.Value) ?? "")}"));
        var line = $"{e.Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{e.Level.ToString().ToUpperInvariant()}] [session={e.SessionId}] [op={e.OperationId ?? "-"}] [module={e.Module}] {e.EventName}: {e.Message}{data}";
        return exception is null ? line : line + Environment.NewLine + Sanitize(exception.ToString());
    }

    private static Dictionary<string, object?> Merge(object? data, Dictionary<string, object?>? extra)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (data != null)
        {
            if (data is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                    result[Convert.ToString(entry.Key) ?? "key"] = Sanitize(Convert.ToString(entry.Value) ?? "");
            }
            else
            {
                foreach (var prop in data.GetType().GetProperties())
                {
                    if (prop.GetIndexParameters().Length > 0)
                        continue;

                    try
                    {
                        result[prop.Name] = Sanitize(Convert.ToString(prop.GetValue(data)) ?? "");
                    }
                    catch
                    {
                        result[prop.Name] = "[UNREADABLE]";
                    }
                }
            }
        }

        if (extra != null)
        {
            foreach (var item in extra)
                result[item.Key] = Sanitize(Convert.ToString(item.Value) ?? "");
        }

        return result;
    }

    internal static string Sanitize(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var redactedKeys = new[] { "access_token", "accessToken", "clientToken", "uuid", "session" };
        foreach (var key in redactedKeys)
            value = System.Text.RegularExpressions.Regex.Replace(value, $"({key}\\s*[=:]\\s*)\\S+", "$1[REDACTED]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // Flags de línea de comandos de Minecraft: --accessToken <jwt>, --accessToken=<jwt>, --clientId <uuid>, etc.
        var redactedFlags = new[] { "accessToken", "access_token", "clientToken", "clientId", "xuid", "uuid" };
        foreach (var flag in redactedFlags)
            value = System.Text.RegularExpressions.Regex.Replace(value, $"--{flag}\\s*[= ]\\s*[^\\s\"]+", $"--{flag}=[REDACTED]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return value;
    }

    private static JsonSerializerOptions JsonOptions(bool writeIndented) => new()
    {
        WriteIndented = writeIndented,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static void AddFileIfExists(ZipArchive zip, string filePath, string entryName)
    {
        if (File.Exists(filePath))
            zip.CreateEntryFromFile(filePath, entryName, CompressionLevel.Fastest);
    }

    private static void AddText(ZipArchive zip, string entryName, string content)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static string BuildMinecraftListing()
    {
        var mcDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        if (!Directory.Exists(mcDir)) return $"{mcDir} does not exist.";

        var lines = new List<string> { mcDir };
        foreach (var path in Directory.EnumerateFileSystemEntries(mcDir, "*", SearchOption.AllDirectories).Take(5000))
        {
            try
            {
                var info = new FileInfo(path);
                lines.Add($"{path} | {(info.Exists ? info.Length : 0)} | {info.LastWriteTime}");
            }
            catch { }
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildDatabaseInfo()
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShoroCraftLauncher", "data", "launcher.db");
        if (!File.Exists(dbPath)) return $"{dbPath} does not exist.";
        var info = new FileInfo(dbPath);
        return $"{dbPath}{Environment.NewLine}Size={info.Length}{Environment.NewLine}Updated={info.LastWriteTime}";
    }

    private sealed record LogWrite(LogEvent? Event = null, Exception? Exception = null, string? MinecraftPath = null, string? Text = null);

    private sealed class OperationScope : IDisposable
    {
        private readonly LogService _owner;
        private readonly string? _previous;
        private readonly string _module;
        private readonly string _operationName;
        private readonly string _operationId;
        private bool _disposed;

        public OperationScope(LogService owner, string? previous, string module, string operationName, string operationId)
        {
            _owner = owner;
            _previous = previous;
            _module = module;
            _operationName = operationName;
            _operationId = operationId;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _owner.Info(_module, $"{_operationName}Completed", $"{_operationName} completado.", new { operationId = _operationId });
            _owner._operationId.Value = _previous;
            _disposed = true;
        }
    }
}
