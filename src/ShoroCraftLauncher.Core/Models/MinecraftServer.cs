using System.ComponentModel;
using System.Runtime.CompilerServices;
using ShoroCraftLauncher.Core.Enums;

namespace ShoroCraftLauncher.Core.Models;

public class MinecraftServer : INotifyPropertyChanged
{
    private ServerStatus _status = ServerStatus.Stopped;

    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ServerType Type { get; set; } = ServerType.Vanilla;
    public string MinecraftVersion { get; set; } = "latest";
    public string? LoaderVersion { get; set; }
    public string DirectoryPath { get; set; } = string.Empty;
    public string JavaPath { get; set; } = string.Empty;
    public int MinRamMB { get; set; } = 1024;
    public int MaxRamMB { get; set; } = 4096;
    public int Port { get; set; } = 25565;
    public string? WorldName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ServerStatus Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string DisplayName => Status == ServerStatus.Running
        ? $"{Name} (en línea)"
        : Name;

    public string StatusText => Status switch
    {
        ServerStatus.Running => "En ejecución",
        ServerStatus.Starting => "Iniciando...",
        ServerStatus.Stopping => "Deteniendo...",
        ServerStatus.Error => "Error",
        _ => "Detenido"
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
