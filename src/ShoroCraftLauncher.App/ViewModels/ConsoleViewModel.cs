using System.Collections.ObjectModel;
using System.Windows.Input;
using ShoroCraftLauncher.App.Commands;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.App.ViewModels;

public class ConsoleViewModel : BaseViewModel, IDisposable
{
    private readonly ILogService _logService;
    private readonly ILauncherService _launcherService;
    private readonly List<LogEvent> _allEvents = new();
    private readonly Action _onGameExited;

    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<string> LevelFilters { get; } = new() { "Todos", "Trace", "Debug", "Info", "Warning", "Error", "Critical" };
    public ObservableCollection<string> ModuleFilters { get; } = new() { "Todos" };

    private string _selectedLevel = "Todos";
    public string SelectedLevel
    {
        get => _selectedLevel;
        set
        {
            if (SetProperty(ref _selectedLevel, value))
                ApplyFilters();
        }
    }

    private string _selectedModule = "Todos";
    public string SelectedModule
    {
        get => _selectedModule;
        set
        {
            if (SetProperty(ref _selectedModule, value))
                ApplyFilters();
        }
    }

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ApplyFilters();
        }
    }

    private bool _isGameRunning;
    public bool IsGameRunning
    {
        get => _isGameRunning;
        set => SetProperty(ref _isGameRunning, value);
    }

    public ICommand ClearLogCommand { get; }
    public ICommand CopyLogCommand { get; }
    public ICommand CopyRelevantLogCommand { get; }

    public ConsoleViewModel(ILogService logService, ILauncherService launcherService)
    {
        _logService = logService;
        _launcherService = launcherService;

        foreach (var logEvent in _logService.RecentEvents)
            AddLogEvent(logEvent, refresh: false);
        ApplyFilters();

        _logService.LogReceived += OnLogReceived;
        _onGameExited = () => IsGameRunning = false;
        _launcherService.GameExited += _onGameExited;
        IsGameRunning = launcherService.IsGameRunning;

        ClearLogCommand = new RelayCommand(_ => ClearLog());
        CopyLogCommand = new RelayCommand(_ => CopyLog());
        CopyRelevantLogCommand = new RelayCommand(_ => CopyRelevantLog());
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

    private void OnLogReceived(object? sender, LogEvent logEvent)
    {
        App.Current.Dispatcher.BeginInvoke(() =>
        {
            AddLogEvent(logEvent);
        });
    }

    public string FullLogText => string.Join(Environment.NewLine, LogLines);

    private void AddLogEvent(LogEvent logEvent, bool refresh = true)
    {
        _allEvents.Add(logEvent);
        if (_allEvents.Count > 3000)
            _allEvents.RemoveRange(0, _allEvents.Count - 3000);

        if (!ModuleFilters.Contains(logEvent.Module))
            ModuleFilters.Add(logEvent.Module);

        if (refresh && PassesFilters(logEvent))
        {
            var cleaned = CleanLogLine(FormatLogEvent(logEvent));
            if (string.IsNullOrWhiteSpace(cleaned)) return;

            LogLines.Add(cleaned);
            if (LogLines.Count > 1000)
                LogLines.RemoveAt(0);

            OnPropertyChanged(nameof(FullLogText));
        }
    }

    private void ApplyFilters()
    {
        LogLines.Clear();
        foreach (var logEvent in _allEvents.Where(PassesFilters).TakeLast(1000))
        {
            var formatted = FormatLogEvent(logEvent);
            var cleaned = CleanLogLine(formatted);
            if (string.IsNullOrWhiteSpace(cleaned) || formatted.Contains("log4j")) continue;
            LogLines.Add(cleaned);
        }

        OnPropertyChanged(nameof(FullLogText));
    }

    private bool PassesFilters(LogEvent logEvent)
    {
        if (SelectedLevel != "Todos" && !logEvent.Level.ToString().Equals(SelectedLevel, StringComparison.OrdinalIgnoreCase))
            return false;

        if (SelectedModule != "Todos" && !logEvent.Module.Equals(SelectedModule, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var text = $"{logEvent.Module} {logEvent.EventName} {logEvent.Message}";
            if (!text.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static string FormatLogEvent(LogEvent logEvent)
    {
        var op = string.IsNullOrEmpty(logEvent.OperationId) ? "-" : logEvent.OperationId;
        return $"{logEvent.Timestamp:HH:mm:ss.fff} [{logEvent.Level}] [{logEvent.Module}] [op={op}] {logEvent.Message}";
    }

    private void AddLogLine(string line)
    {
        var cleaned = CleanLogLine(line);
        if (string.IsNullOrWhiteSpace(cleaned) && line.Contains("log4j")) return;
        if (string.IsNullOrWhiteSpace(cleaned) && string.IsNullOrWhiteSpace(line)) return;

        LogLines.Add(cleaned);

        if (LogLines.Count > 1000)
            LogLines.RemoveAt(0);

        OnPropertyChanged(nameof(FullLogText));
    }

    private void ClearLog()
    {
        SearchText = string.Empty;
        SelectedLevel = "Todos";
        SelectedModule = "Todos";
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

    private void CopyRelevantLog()
    {
        var relevant = _allEvents
            .Where(e => e.Level >= LauncherLogLevel.Warning)
            .TakeLast(200)
            .Select(FormatLogEvent);
        var text = string.Join(Environment.NewLine, relevant);
        if (!string.IsNullOrWhiteSpace(text))
        {
            System.Windows.Clipboard.SetText(text);
            StatusMessage = "Errores y advertencias copiados.";
        }
    }

    public void Dispose()
    {
        _logService.LogReceived -= OnLogReceived;
        _launcherService.GameExited -= _onGameExited;
    }
}
