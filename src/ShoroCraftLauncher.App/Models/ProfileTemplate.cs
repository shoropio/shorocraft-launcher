using ShoroCraftLauncher.Core.Enums;

namespace ShoroCraftLauncher.App.Models;

public sealed class ProfileTemplate
{
    public string Name { get; init; } = string.Empty;
    public ProfileType Type { get; init; }
    public int MinRamMB { get; init; } = 1024;
    public int MaxRamMB { get; init; } = 4096;
    public int WindowWidth { get; init; } = 854;
    public int WindowHeight { get; init; } = 480;
    public string LoaderVersion { get; init; } = string.Empty;

    public static IReadOnlyList<ProfileTemplate> Defaults { get; } = new[]
    {
        new ProfileTemplate { Name = "Vanilla", Type = ProfileType.Vanilla },
        new ProfileTemplate { Name = "OptiFine", Type = ProfileType.OptiFine, LoaderVersion = "latest" },
        new ProfileTemplate { Name = "Forge", Type = ProfileType.Forge, LoaderVersion = "latest" },
        new ProfileTemplate { Name = "NeoForge", Type = ProfileType.NeoForge, LoaderVersion = "latest" },
        new ProfileTemplate { Name = "Fabric", Type = ProfileType.Fabric, LoaderVersion = "latest" },
        new ProfileTemplate { Name = "Quilt", Type = ProfileType.Quilt, LoaderVersion = "latest" },
        new ProfileTemplate { Name = "Iris", Type = ProfileType.Iris, LoaderVersion = "latest" },
    };
}
