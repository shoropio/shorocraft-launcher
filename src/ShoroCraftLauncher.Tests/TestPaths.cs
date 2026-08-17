namespace ShoroCraftLauncher.Tests;

internal static class TestPaths
{
    private const string DataDirEnvironmentVariable = "SHOROCRAFT_DATA_DIR";

    public static string CreateTempDir(string name)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "TestTemp", name, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string GetTempFile(string name, string fileName)
    {
        var dir = CreateTempDir(name);
        return Path.Combine(dir, fileName);
    }

    public static IDisposable UseLauncherDataRoot(string name, out string root)
    {
        root = CreateTempDir(name);
        var previous = Environment.GetEnvironmentVariable(DataDirEnvironmentVariable);
        Environment.SetEnvironmentVariable(DataDirEnvironmentVariable, root);
        return new RestoreEnvironmentVariable(DataDirEnvironmentVariable, previous);
    }

    private sealed class RestoreEnvironmentVariable : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public RestoreEnvironmentVariable(string name, string? previous)
        {
            _name = name;
            _previous = previous;
        }

        public void Dispose()
            => Environment.SetEnvironmentVariable(_name, _previous);
    }
}
