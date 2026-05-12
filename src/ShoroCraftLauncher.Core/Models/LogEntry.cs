using ShoroCraftLauncher.Core.Enums;

namespace ShoroCraftLauncher.Core.Models;

public class LogEntry
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public string Message { get; set; } = string.Empty;
    public LogLevel Level { get; set; } = LogLevel.Info;
    public string Source { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
