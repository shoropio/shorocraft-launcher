using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Core.Interfaces;

public class ServerLaunchResult
{
    public bool Success { get; set; }
    public int ProcessId { get; set; }
    public string? ErrorMessage { get; set; }
}

public interface IServerService
{
    IReadOnlyList<MinecraftServer> Servers { get; }
    event Action? ServersChanged;
    event Action<string>? LogOutput;
    event Action<double, string>? ProgressChanged;
    event Action<ServerStatus>? StatusChanged;

    Task LoadAsync();
    Task<List<string>> GetAvailableVanillaVersionsAsync();
    Task<List<string>> GetAvailablePaperVersionsAsync();
    Task<MinecraftServer> CreateServerAsync(string name, ServerType type, string minecraftVersion, int maxRamMB, string? worldName = null);
    Task DeleteServerAsync(MinecraftServer server);
    Task<ServerLaunchResult> StartAsync(MinecraftServer server);
    Task StopAsync(MinecraftServer server);
    Task StopAllAsync();
    Task SendCommandAsync(MinecraftServer server, string command);
    bool IsRunning(MinecraftServer server);
    IReadOnlyList<string> GetLogHistory(MinecraftServer server);
    Task<string?> GetPublicIpAddressAsync();
}

public interface IServerPluginService
{
    Task<List<ServerPlugin>> GetPluginsAsync(MinecraftServer server);
    Task InstallPluginAsync(MinecraftServer server, ServerPlugin plugin);
    Task DeletePluginAsync(MinecraftServer server, ServerPlugin plugin);
    Task TogglePluginAsync(MinecraftServer server, ServerPlugin plugin);
}
