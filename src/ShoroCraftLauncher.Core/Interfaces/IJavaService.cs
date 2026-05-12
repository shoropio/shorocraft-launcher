namespace ShoroCraftLauncher.Core.Interfaces;

public class JavaInfo
{
    public string Path { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool IsValid { get; set; }
}

public interface IJavaService
{
    Task<List<JavaInfo>> FindJavaInstallationsAsync();
    Task<JavaInfo?> DownloadJavaAsync(IProgress<double>? progress = null);
    Task<string> DownloadJavaForVersionAsync(string minecraftVersion, IProgress<double>? progress = null);
    Task<string> GetRecommendedJavaPathAsync(string minecraftVersion);
}
