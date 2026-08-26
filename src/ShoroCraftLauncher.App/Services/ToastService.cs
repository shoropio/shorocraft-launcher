using System.Collections.ObjectModel;
using System.Windows;
using ShoroCraftLauncher.App.Models;

namespace ShoroCraftLauncher.App.Services;

public class ToastService : IToastService
{
    private readonly object _gate = new();

    public ObservableCollection<ToastItem> Toasts { get; } = new();

    public void ShowToast(ToastItem toast)
    {
        RunOnUiThread(() =>
        {
            lock (_gate)
            {
                if (Toasts.Any(t => t.Id == toast.Id)) return;
                Toasts.Add(toast);
            }
        });
    }

    public ToastItem ShowInfo(string title, string message, TimeSpan? duration = null, IReadOnlyList<ToastAction>? actions = null)
        => ShowCore(title, message, ToastSeverity.Info, duration, actions);

    public ToastItem ShowSuccess(string title, string message, TimeSpan? duration = null, IReadOnlyList<ToastAction>? actions = null)
        => ShowCore(title, message, ToastSeverity.Success, duration, actions);

    public ToastItem ShowWarning(string title, string message, TimeSpan? duration = null, IReadOnlyList<ToastAction>? actions = null)
        => ShowCore(title, message, ToastSeverity.Warning, duration, actions);

    public ToastItem ShowError(string title, string message, TimeSpan? duration = null, IReadOnlyList<ToastAction>? actions = null)
        => ShowCore(title, message, ToastSeverity.Error, duration, actions);

    private ToastItem ShowCore(string title, string message, ToastSeverity severity, TimeSpan? duration, IReadOnlyList<ToastAction>? actions)
    {
        var toast = new ToastItem(title, message, severity, actions, duration, Dismiss);
        RunOnUiThread(() =>
        {
            lock (_gate)
            {
                Toasts.Add(toast);
            }
        });
        return toast;
    }

    public void Dismiss(string id)
    {
        RunOnUiThread(() =>
        {
            lock (_gate)
            {
                var item = Toasts.FirstOrDefault(t => t.Id == id);
                if (item != null) Toasts.Remove(item);
            }
        });
    }

    private static void RunOnUiThread(Action action)
    {
        var app = Application.Current;
        if (app?.Dispatcher != null && !app.Dispatcher.CheckAccess())
            app.Dispatcher.Invoke(action);
        else
            action();
    }
}
