using System;

namespace ShoroCraftLauncher.Core.Models;

public class BackupItem
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string BackupType { get; set; } = string.Empty; // "Worlds", "Scripts", "Configs", "All"
    public DateTime Timestamp { get; set; }
    public long FileSizeBytes { get; set; }
    public string DisplayName => $"{BackupType} - {Timestamp:yyyy-MM-dd HH:mm:ss} ({FileSizeBytes / 1024.0 / 1024.0:F2} MB)";
}
