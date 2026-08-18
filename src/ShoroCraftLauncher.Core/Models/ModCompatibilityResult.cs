using System.Collections.Generic;

namespace ShoroCraftLauncher.Core.Models;

public class ModCompatibilityResult
{
    public int Checked { get; set; }
    public List<string> Disabled { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}