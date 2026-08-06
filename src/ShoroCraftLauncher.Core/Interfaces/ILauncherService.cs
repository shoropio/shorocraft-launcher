using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Core.Interfaces;

public class LaunchResult
{
    public bool Success { get; set; }
    public int ProcessId { get; set; }
    public string? ErrorMessage { get; set; }
}

public interface ILauncherService
{
    Task<LaunchResult> LaunchProfileAsync(Profile profile, AuthResult auth);
    Task StopGameAsync();
    bool IsGameRunning { get; }
    IReadOnlyList<string> LogHistory { get; }
    event Action<string>? LogOutput;
    event Action<double, string>? ProgressChanged;
    event Action? ProgressCompleted;
    event Action? GameExited;
    void Log(string message);
}
