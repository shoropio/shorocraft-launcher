using System.Text.Json.Serialization;

namespace ShoroCraftLauncher.Core.Models;

public class MrpackIndex
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("versionId")]
    public string? VersionId { get; set; }

    [JsonPropertyName("game")]
    public string? Game { get; set; }

    [JsonPropertyName("files")]
    public List<MrpackFile> Files { get; set; } = new();

    [JsonPropertyName("dependencies")]
    public Dictionary<string, string> Dependencies { get; set; } = new();
}

public class MrpackFile
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("hashes")]
    public MrpackHashes? Hashes { get; set; }

    [JsonPropertyName("downloads")]
    public List<string> Downloads { get; set; } = new();

    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }

    [JsonPropertyName("env")]
    public MrpackEnv? Env { get; set; }
}

public class MrpackHashes
{
    [JsonPropertyName("sha1")]
    public string? Sha1 { get; set; }

    [JsonPropertyName("sha512")]
    public string? Sha512 { get; set; }
}

public class MrpackEnv
{
    [JsonPropertyName("client")]
    public string? Client { get; set; }

    [JsonPropertyName("server")]
    public string? Server { get; set; }
}

public class ModpackImportResult
{
    public string ModpackName { get; set; } = string.Empty;
    public int ModsInstalled { get; set; }
    public int FilesInstalled { get; set; }
    public string? MinecraftVersion { get; set; }
    public string? RequiredLoader { get; set; }
    public List<string> Warnings { get; set; } = new();
}
