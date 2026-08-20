using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ShoroCraftLauncher.Core.Enums;

namespace ShoroCraftLauncher.Core.Models;

public class Mod : INotifyPropertyChanged
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string MinecraftVersion { get; set; } = string.Empty;
    public string ModVersion { get; set; } = string.Empty;
    public string? LatestVersion { get; set; }
    public bool HasUpdate { get; set; }
    public string? UpdateStatusText { get; set; }
    public string? UpdateAvailableText { get; set; }
    public string? Description { get; set; }
    public string? IconPath { get; set; }
    public string? SourceProvider { get; set; }
    public string? RemoteProjectId { get; set; }
    public string? RemoteSlug { get; set; }

    private ModStatus _status = ModStatus.Active;
    public ModStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
                OnPropertyChanged(nameof(Status));
        }
    }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;

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

    public IDisposable SubscribeToStatusChange(Func<Task> onChange)
    {
        PropertyChangedEventHandler handler = async (_, e) =>
        {
            if (e.PropertyName == nameof(Status))
                await onChange();
        };
        PropertyChanged += handler;
        return new ActionDisposable(() => PropertyChanged -= handler);
    }

    private sealed class ActionDisposable : IDisposable
    {
        private readonly Action _action;
        public ActionDisposable(Action action) => _action = action;
        public void Dispose() => _action();
    }
}
