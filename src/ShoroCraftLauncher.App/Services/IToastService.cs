using System.Collections.ObjectModel;
using ShoroCraftLauncher.App.Models;

namespace ShoroCraftLauncher.App.Services;

public interface IToastService
{
    ObservableCollection<ToastItem> Toasts { get; }

    void ShowToast(ToastItem toast);

    ToastItem ShowInfo(string title, string message, TimeSpan? duration = null, IReadOnlyList<ToastAction>? actions = null);
    ToastItem ShowSuccess(string title, string message, TimeSpan? duration = null, IReadOnlyList<ToastAction>? actions = null);
    ToastItem ShowWarning(string title, string message, TimeSpan? duration = null, IReadOnlyList<ToastAction>? actions = null);
    ToastItem ShowError(string title, string message, TimeSpan? duration = null, IReadOnlyList<ToastAction>? actions = null);

    void Dismiss(string id);
}
