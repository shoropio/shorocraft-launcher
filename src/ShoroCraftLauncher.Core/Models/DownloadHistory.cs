namespace ShoroCraftLauncher.Core.Models;

public class DownloadHistory
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public long BytesDownloaded { get; set; }
    public bool Success { get; set; }
    public DateTime DownloadedAt { get; set; } = DateTime.UtcNow;
}
