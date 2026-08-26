using System.Windows.Input;
using ShoroCraftLauncher.App.Commands;

namespace ShoroCraftLauncher.App.Models;

public enum ToastSeverity
{
    Info,
    Success,
    Warning,
    Error
}

public class ToastAction
{
    public string Label { get; }
    public ICommand Command { get; }

    public ToastAction(string label, ICommand command)
    {
        Label = label;
        Command = command;
    }
}

public class ToastItem
{
    private readonly Action<string> _dismiss;

    public string Id { get; } = Guid.NewGuid().ToString();
    public string Title { get; }
    public string Message { get; }
    public ToastSeverity Severity { get; }
    public IReadOnlyList<ToastAction> Actions { get; set; } = Array.Empty<ToastAction>();
    public TimeSpan? Duration { get; }
    public ICommand DismissCommand { get; }

    public ToastItem(string title, string message, ToastSeverity severity,
        IReadOnlyList<ToastAction>? actions = null, TimeSpan? duration = null, Action<string>? dismiss = null)
    {
        Title = title;
        Message = message;
        Severity = severity;
        Actions = actions ?? Array.Empty<ToastAction>();
        Duration = duration;
        _dismiss = dismiss ?? (_ => { });
        DismissCommand = new RelayCommand(_ => _dismiss(Id));
    }
}
