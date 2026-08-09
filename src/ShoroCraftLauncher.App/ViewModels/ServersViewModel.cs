using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.App.Commands;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.App.ViewModels;

public class ServersViewModel : BaseViewModel, IDisposable
{
    private readonly IServerService _serverService;
    private readonly ILogger<ServersViewModel> _logger;

    private readonly Action _serversChangedHandler;
    private readonly Action<string> _logOutputHandler;
    private readonly Action<ServerStatus> _statusChangedHandler;
    private readonly Action<double, string> _progressChangedHandler;

    public ObservableCollection<MinecraftServer> Servers { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<ServerType> ServerTypes { get; } = new() { ServerType.Vanilla, ServerType.Paper };

    private string _newServerName = string.Empty;
    public string NewServerName
    {
        get => _newServerName;
        set => SetProperty(ref _newServerName, value);
    }

    private ServerType _selectedServerType = ServerType.Vanilla;
    public ServerType SelectedServerType
    {
        get => _selectedServerType;
        set
        {
            if (SetProperty(ref _selectedServerType, value))
                _ = LoadVersionsAsync();
        }
    }

    private ObservableCollection<string> _availableVersions = new();
    public ObservableCollection<string> AvailableVersions
    {
        get => _availableVersions;
        set => SetProperty(ref _availableVersions, value);
    }

    private string? _selectedVersion;
    public string? SelectedVersion
    {
        get => _selectedVersion;
        set => SetProperty(ref _selectedVersion, value);
    }

    private int _maxRamMB = 4096;
    public int MaxRamMB
    {
        get => _maxRamMB;
        set => SetProperty(ref _maxRamMB, value);
    }

    private MinecraftServer? _selectedServer;
    public MinecraftServer? SelectedServer
    {
        get => _selectedServer;
        set
        {
        if (SetProperty(ref _selectedServer, value))
        {
            LoadServerLogs(value);
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(CanControl));
            CommandManager.InvalidateRequerySuggested();
        }
        }
    }

    public bool IsSelected => SelectedServer != null;
    public bool CanControl => SelectedServer != null;

    private string _commandText = string.Empty;
    public string CommandText
    {
        get => _commandText;
        set => SetProperty(ref _commandText, value);
    }

    private double _downloadProgress;
    public double DownloadProgress
    {
        get => _downloadProgress;
        set => SetProperty(ref _downloadProgress, value);
    }

    private bool _isDownloading;
    public bool IsDownloading
    {
        get => _isDownloading;
        set => SetProperty(ref _isDownloading, value);
    }

    private string _downloadStatus = string.Empty;
    public string DownloadStatus
    {
        get => _downloadStatus;
        set => SetProperty(ref _downloadStatus, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand CreateServerCommand { get; }
    public ICommand DeleteServerCommand { get; }
    public ICommand StartServerCommand { get; }
    public ICommand StopServerCommand { get; }
    public ICommand WakeServerCommand { get; }
    public ICommand CopyConsoleCommand { get; }
    public ICommand ClearConsoleCommand { get; }
    public ICommand SendCommandCommand { get; }

    public ServersViewModel(IServerService serverService, ILogger<ServersViewModel> logger)
    {
        _serverService = serverService;
        _logger = logger;

        RefreshCommand = new RelayCommand(async _ => await LoadAsync());
        CreateServerCommand = new RelayCommand(async _ => await CreateServer());
        DeleteServerCommand = new RelayCommand(async p => await DeleteServer(p), _ => IsSelected);
        StartServerCommand = new RelayCommand(async p => await StartServer(p), _ => IsSelected);
        StopServerCommand = new RelayCommand(async p => await StopServer(p), _ => IsSelected);
        WakeServerCommand = new RelayCommand(async _ => await WakeServer(), _ => IsSelected);
        CopyConsoleCommand = new RelayCommand(_ => CopyConsole(), _ => LogLines.Count > 0);
        ClearConsoleCommand = new RelayCommand(_ => ClearConsole(), _ => LogLines.Count > 0);
        SendCommandCommand = new RelayCommand(async _ => await SendCommand());

        _serversChangedHandler = () => Dispatcher(() => SyncServers());
        _logOutputHandler = line => Dispatcher(() => AddLogLine(line));
        _statusChangedHandler = _ => Dispatcher(() =>
        {
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(CanControl));
        });
        _progressChangedHandler = (pct, msg) => Dispatcher(() =>
        {
            IsDownloading = true;
            DownloadProgress = pct >= 0 ? pct : 0;
            DownloadStatus = msg;
        });

        _serverService.ServersChanged += _serversChangedHandler;
        _serverService.LogOutput += _logOutputHandler;
        _serverService.StatusChanged += _statusChangedHandler;
        _serverService.ProgressChanged += _progressChangedHandler;

        _ = LoadAsync();
        _ = LoadVersionsAsync();
    }

    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            await _serverService.LoadAsync();
            SyncServers();
            StatusMessage = $"{Servers.Count} servidores.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load servers");
            StatusMessage = $"Error: {ex.Message}";
        }
        IsBusy = false;
    }

    private async Task LoadVersionsAsync()
    {
        var versions = SelectedServerType == ServerType.Paper
            ? await _serverService.GetAvailablePaperVersionsAsync()
            : await _serverService.GetAvailableVanillaVersionsAsync();

        AvailableVersions = new ObservableCollection<string>(versions);
        SelectedVersion = versions.FirstOrDefault();
    }

    private void SyncServers()
    {
        var currentIds = Servers.Select(s => s.Id).ToHashSet();
        foreach (var server in _serverService.Servers)
        {
            if (!currentIds.Contains(server.Id))
                Servers.Add(server);
        }

        var removed = Servers.Where(s => !_serverService.Servers.Any(x => x.Id == s.Id)).ToList();
        foreach (var server in removed)
        {
            if (SelectedServer?.Id == server.Id) SelectedServer = null;
            Servers.Remove(server);
        }

        if (SelectedServer == null && Servers.Count > 0)
            SelectedServer = Servers[0];
    }

    private void LoadServerLogs(MinecraftServer? server)
    {
        LogLines.Clear();
        if (server == null) return;
        foreach (var line in _serverService.GetLogHistory(server))
            LogLines.Add(line);

        if (LogLines.Count > 0)
            OnPropertyChanged(nameof(LogLines));
    }

    private void AddLogLine(string line)
    {
        if (SelectedServer == null) return;
        LogLines.Add(line);
        if (LogLines.Count > 2000)
            LogLines.RemoveAt(0);
    }

    private async Task CreateServer()
    {
        if (string.IsNullOrWhiteSpace(NewServerName))
        {
            StatusMessage = "Introduce un nombre para el servidor.";
            return;
        }
        if (string.IsNullOrWhiteSpace(SelectedVersion))
        {
            StatusMessage = "Selecciona una versión de Minecraft.";
            return;
        }

        IsBusy = true;
        try
        {
            var server = await _serverService.CreateServerAsync(
                NewServerName.Trim(),
                SelectedServerType,
                SelectedVersion,
                MaxRamMB);

            SelectedServer = server;
            NewServerName = string.Empty;
            StatusMessage = $"Servidor '{server.Name}' creado.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create server");
            StatusMessage = $"Error: {ex.Message}";
        }
        IsBusy = false;
    }

    private async Task DeleteServer(object? param)
    {
        if (param is not MinecraftServer server) return;

        var result = System.Windows.MessageBox.Show(
            $"¿Eliminar el servidor '{server.Name}'? Se borrará su carpeta y todos sus datos.",
            "Eliminar servidor",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (result != System.Windows.MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            await _serverService.DeleteServerAsync(server);
            StatusMessage = $"Servidor '{server.Name}' eliminado.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete server");
            StatusMessage = $"Error: {ex.Message}";
        }
        IsBusy = false;
    }

    private async Task StartServer(object? param)
    {
        var server = param as MinecraftServer ?? SelectedServer;
        if (server == null) return;

        IsBusy = true;
        IsDownloading = true;
        try
        {
            var result = await _serverService.StartAsync(server);
            StatusMessage = result.Success
                ? $"Servidor iniciado (PID {result.ProcessId})."
                : $"Error al iniciar: {result.ErrorMessage}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start server");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsDownloading = false;
            IsBusy = false;
        }
    }

    private async Task StopServer(object? param)
    {
        var server = param as MinecraftServer ?? SelectedServer;
        if (server == null) return;

        IsBusy = true;
        try
        {
            await _serverService.StopAsync(server);
            StatusMessage = "Servidor detenido.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop server");
            StatusMessage = $"Error: {ex.Message}";
        }
        IsBusy = false;
    }

    private async Task SendCommand()
    {
        if (SelectedServer == null || string.IsNullOrWhiteSpace(CommandText)) return;

        var command = CommandText.Trim();
        LogLines.Add($"> {command}");
        await _serverService.SendCommandAsync(SelectedServer, command);
        CommandText = string.Empty;
    }

    private async Task WakeServer()
    {
        if (SelectedServer == null) return;

        LogLines.Add("> list");
        await _serverService.SendCommandAsync(SelectedServer, "list");
        StatusMessage = "Comando 'list' enviado para despertar el servidor.";
    }

    private void CopyConsole()
    {
        var text = string.Join(Environment.NewLine, LogLines);
        if (string.IsNullOrWhiteSpace(text)) return;

        System.Windows.Clipboard.SetText(text);
        StatusMessage = "Consola copiada al portapapeles.";
    }

    private void ClearConsole()
    {
        LogLines.Clear();
        StatusMessage = "Consola limpiada.";
    }

    private static void Dispatcher(Action action)
    {
        var app = System.Windows.Application.Current;
        if (app == null) return;
        if (app.Dispatcher.CheckAccess())
            action();
        else
            app.Dispatcher.BeginInvoke(action);
    }

    public void Dispose()
    {
        _serverService.ServersChanged -= _serversChangedHandler;
        _serverService.LogOutput -= _logOutputHandler;
        _serverService.StatusChanged -= _statusChangedHandler;
        _serverService.ProgressChanged -= _progressChangedHandler;
    }
}
