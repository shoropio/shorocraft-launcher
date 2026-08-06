namespace ShoroCraftLauncher.Core.Models;

public class ShaderPackSearchResult
{
    public string ProjectId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? IconPath { get; set; }
    public string ModVersion { get; set; } = "latest";
}
