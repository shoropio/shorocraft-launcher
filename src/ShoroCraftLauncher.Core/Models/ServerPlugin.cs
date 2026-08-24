using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ShoroCraftLauncher.Core.Models;

public class ServerPlugin : INotifyPropertyChanged
{
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? ProjectId { get; set; }
    public bool IsKnown { get; set; }

    private bool _isInstalled;
    public bool IsInstalled
    {
        get => _isInstalled;
        set => SetProperty(ref _isInstalled, value);
    }

    private bool _hasUpdate;
    public bool HasUpdate
    {
        get => _hasUpdate;
        set => SetProperty(ref _hasUpdate, value);
    }

    private bool _isDisabled;
    public bool IsDisabled
    {
        get => _isDisabled;
        set => SetProperty(ref _isDisabled, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
