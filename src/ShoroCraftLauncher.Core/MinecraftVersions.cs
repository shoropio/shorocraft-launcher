namespace ShoroCraftLauncher.Core;

/// <summary>
/// Utilidades de normalización de versiones de Minecraft.
/// El nuevo sistema de versionado Mojang (26.x) se mapea al formato
/// clásico (1.21.x) para las APIs de mods (Modrinth/CurseForge).
/// </summary>
public static class MinecraftVersions
{
    public static string ToModrinthVersion(string mcVersion)
    {
        if (string.IsNullOrWhiteSpace(mcVersion)) return mcVersion;
        var trimmed = mcVersion.Trim();
        if (trimmed.StartsWith("26.", StringComparison.OrdinalIgnoreCase))
        {
            var parts = trimmed.Split('.');
            if (parts.Length >= 2 && int.TryParse(parts[1], out var minor))
            {
                return $"1.21.{minor}";
            }
        }
        return trimmed;
    }
}
