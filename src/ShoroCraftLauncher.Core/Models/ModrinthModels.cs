namespace ShoroCraftLauncher.Core.Models;

public record ModrinthDependency(string ProjectId, string VersionId, string DependencyType);

public record ModrinthVersionInfo(
    string Url,
    string FileName,
    string Version,
    long Size,
    string ProjectSlug,
    IReadOnlyList<ModrinthDependency> Dependencies);
