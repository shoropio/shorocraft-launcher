using ShoroCraftLauncher.Core.Enums;

namespace ShoroCraftLauncher.Core.Models;

public class Mod
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string MinecraftVersion { get; set; } = string.Empty;
    public string ModVersion { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? IconPath { get; set; }
    public ModStatus Status { get; set; } = ModStatus.Active;
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
