namespace ShoroCraftLauncher.Infrastructure;

internal static class LauncherPaths
{
    private const string DataDirEnvironmentVariable = "SHOROCRAFT_DATA_DIR";

    private static readonly object CacheLock = new();
    private static string? _cachedKey;
    private static string _cachedRoot = DefaultRoot;

    public static string DataRoot => GetValidatedRoot();

    private static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShoroCraftLauncher");

    public static string GetPath(params string[] parts)
        => Path.Combine(new[] { DataRoot }.Concat(parts).ToArray());

    private static string GetValidatedRoot()
    {
        var key = Environment.GetEnvironmentVariable(DataDirEnvironmentVariable) ?? string.Empty;
        lock (CacheLock)
        {
            if (_cachedKey == key)
                return _cachedRoot;

            var resolved = ResolveDataRoot(key);
            _cachedKey = key;
            _cachedRoot = resolved;
            return resolved;
        }
    }

    private static string ResolveDataRoot(string overridePath)
    {
        if (string.IsNullOrWhiteSpace(overridePath))
            return DefaultRoot;

        try
        {
            if (!Path.IsPathRooted(overridePath))
                return DefaultRoot;

            var full = Path.GetFullPath(overridePath);
            Directory.CreateDirectory(full);
            var probe = Path.Combine(full, ".shorocraft_access_test");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
            return full;
        }
        catch
        {
            return DefaultRoot;
        }
    }
}
