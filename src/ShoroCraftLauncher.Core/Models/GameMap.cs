using ShoroCraftLauncher.Core.Enums;

namespace ShoroCraftLauncher.Core.Models;

public class GameMap
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string? PreviewImagePath { get; set; }
    public PackStatus Status { get; set; } = PackStatus.Active;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
