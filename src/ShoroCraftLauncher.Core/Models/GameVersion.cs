namespace ShoroCraftLauncher.Core.Models;

public class GameVersion
{
    public int Id { get; set; }
    public string VersionId { get; set; } = string.Empty;
    public string VersionType { get; set; } = "release";
    public string Url { get; set; } = string.Empty;
    public DateTime ReleasedAt { get; set; }
    public bool IsInstalled { get; set; } = false;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
