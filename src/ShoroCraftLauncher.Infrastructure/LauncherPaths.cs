namespace ShoroCraftLauncher.Infrastructure;

internal static class LauncherPaths
{
    private const string DataDirEnvironmentVariable = "SHOROCRAFT_DATA_DIR";

    public static string DataRoot
    {
        get
        {
            var overridePath = Environment.GetEnvironmentVariable(DataDirEnvironmentVariable);
            return string.IsNullOrWhiteSpace(overridePath)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShoroCraftLauncher")
                : overridePath;
        }
    }

    public static string GetPath(params string[] parts)
        => Path.Combine(new[] { DataRoot }.Concat(parts).ToArray());
}
