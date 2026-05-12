using System.Collections.ObjectModel;
using System.Windows.Input;
using ShoroCraftLauncher.App.Commands;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.App.ViewModels;

public class ConsoleViewModel : BaseViewModel
{
    private readonly ILauncherService _launcherService;

    public ObservableCollection<string> LogLines { get; } = new();

    private bool _isGameRunning;
    public bool IsGameRunning
    {
        get => _isGameRunning;
        set => SetProperty(ref _isGameRunning, value);
    }

    public ICommand ClearLogCommand { get; }
    public ICommand CopyLogCommand { get; }

    public ConsoleViewModel(ILauncherService launcherService)
    {
        _launcherService = launcherService;

        _launcherService.LogOutput += OnLogOutput;
        _launcherService.GameExited += () => IsGameRunning = false;

        ClearLogCommand = new RelayCommand(_ => ClearLog());
        CopyLogCommand = new RelayCommand(_ => CopyLog());
    }

    private string CleanLogLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return line;

        var trimmed = line.Trim();
        if (trimmed.StartsWith("<log4j:Event") || trimmed == "</log4j:Event>")
            return string.Empty;

        var cleaned = line.Replace("<![CDATA[", "")
                          .Replace("]]>", "")
                          .Replace("</log4j:Message>", "")
                          .Replace("<log4j:Message>", "")
                          .Replace("</log4j:Throwable>", "")
                          .Replace("<log4j:Throwable>", "");

        return cleaned;
    }

    private void OnLogOutput(string line)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            var cleaned = CleanLogLine(line);
            if (string.IsNullOrWhiteSpace(cleaned) && line.Contains("log4j")) return;
            if (string.IsNullOrWhiteSpace(cleaned) && string.IsNullOrWhiteSpace(line)) return;

            LogLines.Add(cleaned);

            if (LogLines.Count > 1000)
            {
                LogLines.RemoveAt(0);
            }

            IsGameRunning = _launcherService.IsGameRunning;
        });
    }

    public string FullLogText => string.Join(Environment.NewLine, LogLines);

    private void ClearLog()
    {
        LogLines.Clear();
    }

    private void CopyLog()
    {
        var text = FullLogText;
        if (!string.IsNullOrEmpty(text))
        {
            System.Windows.Clipboard.SetText(text);
            StatusMessage = "Log copiado al portapapeles.";
        }
    }
}
