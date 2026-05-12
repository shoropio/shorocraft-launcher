using ShoroCraftLauncher.Core.Enums;

namespace ShoroCraftLauncher.Core.Models;

public class Profile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MinecraftVersion { get; set; } = "latest";
    public ProfileType Type { get; set; } = ProfileType.Vanilla;
    public string GameDirectory { get; set; } = string.Empty;
    public string JavaPath { get; set; } = string.Empty;
    public int MinRamMB { get; set; } = 1024;
    public int MaxRamMB { get; set; } = 4096;
    public string JvmArguments { get; set; } = string.Empty;
    public int WindowWidth { get; set; } = 854;
    public int WindowHeight { get; set; } = 480;
    public string IconPath { get; set; } = string.Empty;
    public string LoaderVersion { get; set; } = string.Empty;
    public bool IsFullscreen { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
