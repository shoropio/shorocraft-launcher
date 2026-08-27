using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.App.Commands;
using ShoroCraftLauncher.App.Services;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.App.ViewModels;

public class ServersViewModel : BaseViewModel, IDisposable
{
    private readonly IServerService _serverService;
    private readonly IServerPluginService _pluginService;
    private readonly ILogger<ServersViewModel> _logger;

    private readonly Action _serversChangedHandler;
    private readonly Action<string> _logOutputHandler;
    private readonly Action<ServerStatus> _statusChangedHandler;
    private readonly Action<double, string> _progressChangedHandler;

    public ObservableCollection<MinecraftServer> Servers { get; } = new();
    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<ServerPlugin> ServerPlugins { get; } = new();
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

    private bool _newServerOnlineMode = true;
    public bool NewServerOnlineMode
    {
        get => _newServerOnlineMode;
        set => SetProperty(ref _newServerOnlineMode, value);
    }

    private bool _onlineMode = true;
    public bool OnlineMode
    {
        get => _onlineMode;
        set => SetProperty(ref _onlineMode, value);
    }

    private string _serverPropertiesText = string.Empty;
    public string ServerPropertiesText
    {
        get => _serverPropertiesText;
        set => SetProperty(ref _serverPropertiesText, value);
    }

    public string ServerActionLabel => SelectedServer?.Status == ServerStatus.Running ? "Detener" : "Iniciar";

    public string ServerConsoleText => string.Join(Environment.NewLine, LogLines);

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
            OnPropertyChanged(nameof(ShowPlugins));
            OnPropertyChanged(nameof(LocalAddress));
            OnPropertyChanged(nameof(PublicAddress));
            OnPropertyChanged(nameof(ServerAddress));
            OnPropertyChanged(nameof(ServerActionLabel));
            OnPropertyChanged(nameof(ServerConsoleText));
            CommandManager.InvalidateRequerySuggested();
            _ = LoadPluginsAsync(value);
            _ = LoadOnlineModeAsync(value);
            _ = LoadServerPropertiesAsync(value);
            UpdateConnectionInfo(value);
        }
        }
    }

    public bool IsSelected => SelectedServer != null;
    public bool CanControl => SelectedServer != null;
    public bool ShowPlugins => SelectedServer != null && SelectedServer.Type == ServerType.Paper;

    private string _localIp = string.Empty;
    public string LocalIp
    {
        get => _localIp;
        set
        {
            if (SetProperty(ref _localIp, value))
                OnPropertyChanged(nameof(LocalAddress));
        }
    }

    private string? _publicIp;
    public string? PublicIp
    {
        get => _publicIp;
        set
        {
            if (SetProperty(ref _publicIp, value))
                OnPropertyChanged(nameof(ServerAddress));
        }
    }

    public string ServerAddress
    {
        get
        {
            if (SelectedServer == null) return string.Empty;
            var host = !string.IsNullOrEmpty(PublicIp) ? PublicIp! : LocalIp;
            return string.IsNullOrEmpty(host) ? string.Empty : $"{host}:{SelectedServer.Port}";
        }
    }

    public string LocalAddress
    {
        get
        {
            if (SelectedServer == null) return string.Empty;
            return string.IsNullOrEmpty(LocalIp) ? string.Empty : $"{LocalIp}:{SelectedServer.Port}";
        }
    }

    public string? PublicAddress
    {
        get
        {
            if (SelectedServer == null || string.IsNullOrEmpty(PublicIp)) return null;
            return $"{PublicIp}:{SelectedServer.Port}";
        }
    }

    private string _commandText = string.Empty;
    public string CommandText
    {
        get => _commandText;
        set
        {
            if (SetProperty(ref _commandText, value))
                CommandManager.InvalidateRequerySuggested();
        }
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
    public ICommand ToggleServerCommand { get; }
    public ICommand CopyConsoleCommand { get; }
    public ICommand ClearConsoleCommand { get; }
    public ICommand SendCommandCommand { get; }
    public ICommand InstallPluginCommand { get; }
    public ICommand TogglePluginCommand { get; }
    public ICommand DeletePluginCommand { get; }
    public ICommand RefreshPluginsCommand { get; }
        public ICommand CopyAddressCommand { get; }
        public ICommand ApplyOnlineModeCommand { get; }
        public ICommand SaveServerPropertiesCommand { get; }

    public ServersViewModel(IServerService serverService, IServerPluginService pluginService, ILogger<ServersViewModel> logger)
    {
        _serverService = serverService;
        _pluginService = pluginService;
        _logger = logger;

        RefreshCommand = new RelayCommand(async _ => await LoadAsync());
        CreateServerCommand = new RelayCommand(async _ => await CreateServer());
        DeleteServerCommand = new RelayCommand(async p => await DeleteServer(p), _ => IsSelected);
        ToggleServerCommand = new RelayCommand(async p => await ToggleServer(p), _ => IsSelected);
        CopyConsoleCommand = new RelayCommand(_ => CopyConsole(), _ => LogLines.Count > 0);
        ClearConsoleCommand = new RelayCommand(_ => ClearConsole(), _ => LogLines.Count > 0);
        SendCommandCommand = new RelayCommand(async _ => await SendCommand(), _ => IsSelected && !string.IsNullOrWhiteSpace(CommandText));
        InstallPluginCommand = new RelayCommand(async p => await InstallOrUpdatePlugin(p), _ => IsSelected && !IsBusy);
        TogglePluginCommand = new RelayCommand(async p => await TogglePlugin(p), _ => IsSelected && !IsBusy);
        DeletePluginCommand = new RelayCommand(async p => await DeletePlugin(p), _ => IsSelected && !IsBusy);
        RefreshPluginsCommand = new RelayCommand(async _ => await LoadPluginsAsync(SelectedServer), _ => IsSelected && !IsBusy);
        CopyAddressCommand = new RelayCommand(_ => CopyServerAddress(), _ => IsSelected && !string.IsNullOrEmpty(ServerAddress));
        ApplyOnlineModeCommand = new RelayCommand(async _ => await ApplyOnlineMode(), _ => IsSelected);
        SaveServerPropertiesCommand = new RelayCommand(async _ => await SaveServerProperties(), _ => IsSelected);

        _serversChangedHandler = () => Dispatcher(() => SyncServers());
        _logOutputHandler = line => Dispatcher(() => AddLogLine(line));
        _statusChangedHandler = _ => Dispatcher(() =>
        {
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(CanControl));
            OnPropertyChanged(nameof(ServerActionLabel));
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
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadVersionsAsync()
    {
        try
        {
            var versions = SelectedServerType == ServerType.Paper
                ? await _serverService.GetAvailablePaperVersionsAsync()
                : await _serverService.GetAvailableVanillaVersionsAsync();

            AvailableVersions = new ObservableCollection<string>(versions);
            SelectedVersion = versions.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load server versions");
            StatusMessage = "Error al obtener versiones del servidor.";
        }
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

    private async Task LoadPluginsAsync(MinecraftServer? server)
    {
        if (server == null || server.Type != ServerType.Paper)
        {
            Dispatcher(() => ServerPlugins.Clear());
            return;
        }

        List<ServerPlugin> plugins;
        try
        {
            plugins = await _pluginService.GetPluginsAsync(server).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load server plugins");
            return;
        }

        Dispatcher(() =>
        {
            ServerPlugins.Clear();
            foreach (var p in plugins)
                ServerPlugins.Add(p);
        });
    }

    private async Task InstallOrUpdatePlugin(object? parameter)
    {
        if (parameter is not ServerPlugin plugin || SelectedServer == null)
            return;

        IsBusy = true;
        try
        {
            StatusMessage = $"Instalando {plugin.Name}...";
            await _pluginService.InstallPluginAsync(SelectedServer, plugin).ConfigureAwait(false);
            await LoadPluginsAsync(SelectedServer);
            StatusMessage = $"{plugin.Name} instalado correctamente.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install plugin");
            StatusMessage = $"Error al instalar {plugin.Name}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task TogglePlugin(object? parameter)
    {
        if (parameter is not ServerPlugin plugin || SelectedServer == null)
            return;

        IsBusy = true;
        try
        {
            await _pluginService.TogglePluginAsync(SelectedServer, plugin).ConfigureAwait(false);
            await LoadPluginsAsync(SelectedServer);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle plugin");
            StatusMessage = $"Error al cambiar estado de {plugin.Name}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeletePlugin(object? parameter)
    {
        if (parameter is not ServerPlugin plugin || SelectedServer == null)
            return;

        var result = DialogHelper.Confirm(
            $"¿Eliminar el plugin '{plugin.Name}'?", "Eliminar plugin");
        if (result != System.Windows.MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            await _pluginService.DeletePluginAsync(SelectedServer, plugin).ConfigureAwait(false);
            await LoadPluginsAsync(SelectedServer);
            StatusMessage = $"Plugin '{plugin.Name}' eliminado.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete plugin");
            StatusMessage = $"Error al eliminar {plugin.Name}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpdateConnectionInfo(MinecraftServer? server)
    {
        if (server == null)
        {
            LocalIp = string.Empty;
            PublicIp = null;
            OnPropertyChanged(nameof(LocalAddress));
            OnPropertyChanged(nameof(PublicAddress));
            OnPropertyChanged(nameof(ServerAddress));
            return;
        }

        LocalIp = GetLocalIpAddress();
        OnPropertyChanged(nameof(ServerAddress));
        _ = LoadPublicIpAsync();
    }

    private async Task LoadOnlineModeAsync(MinecraftServer? server)
    {
        if (server == null)
        {
            OnlineMode = true;
            return;
        }
        OnlineMode = await _serverService.GetOnlineModeAsync(server).ConfigureAwait(false);
    }

    private async Task ApplyOnlineMode()
    {
        if (SelectedServer == null) return;
        try
        {
            await _serverService.SetOnlineModeAsync(SelectedServer, OnlineMode).ConfigureAwait(false);
            await RefreshServerPropertiesAsync(SelectedServer);
            StatusMessage = SelectedServer.Status == ServerStatus.Running
                ? "Modo de verificación de cuentas actualizado; se aplicará al reiniciar el servidor."
                : "Modo de verificación de cuentas actualizado.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply online-mode");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    private async Task LoadServerPropertiesAsync(MinecraftServer? server)
    {
        if (server == null)
        {
            ServerPropertiesText = string.Empty;
            OnlineMode = true;
            return;
        }
        await RefreshServerPropertiesAsync(server);
    }

    private async Task RefreshServerPropertiesAsync(MinecraftServer server)
    {
        var content = await _serverService.GetServerPropertiesAsync(server).ConfigureAwait(false);
        content ??= string.Empty;
        ServerPropertiesText = content;
        OnlineMode = ParseOnlineMode(content);
    }

    private static bool ParseOnlineMode(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (!line.StartsWith("online-mode", StringComparison.OrdinalIgnoreCase))
                continue;

            var eq = line.IndexOf('=');
            if (eq < 0) continue;

            var value = line[(eq + 1)..].Trim();
            return value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private async Task SaveServerProperties()
    {
        if (SelectedServer == null) return;
        try
        {
            await _serverService.SaveServerPropertiesAsync(SelectedServer, ServerPropertiesText).ConfigureAwait(false);
            OnlineMode = ParseOnlineMode(ServerPropertiesText);
            StatusMessage = SelectedServer.Status == ServerStatus.Running
                ? "server.properties guardado; se aplicará al reiniciar el servidor."
                : "server.properties guardado.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save server.properties");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    private async Task LoadPublicIpAsync()
    {
        try
        {
            PublicIp = await _serverService.GetPublicIpAddressAsync().ConfigureAwait(false);
        }
        catch
        {
            PublicIp = null;
        }
    }

    private static string GetLocalIpAddress()
    {
        try
        {
            var host = System.Net.Dns.GetHostName();
            var addresses = System.Net.Dns.GetHostAddresses(host)
                .Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Where(ip => !System.Net.IPAddress.IsLoopback(ip))
                .ToList();
            return addresses.Count > 0 ? addresses[0].ToString() : "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    private void CopyServerAddress()
    {
        if (string.IsNullOrEmpty(ServerAddress)) return;
        System.Windows.Clipboard.SetText(ServerAddress);
        StatusMessage = $"Dirección del servidor copiada: {ServerAddress}";
        CommandManager.InvalidateRequerySuggested();
    }

    private void LoadServerLogs(MinecraftServer? server)
    {
        LogLines.Clear();
        if (server == null) return;
        foreach (var line in _serverService.GetLogHistory(server))
            LogLines.Add(line);

        if (LogLines.Count > 0)
        {
            OnPropertyChanged(nameof(LogLines));
            OnPropertyChanged(nameof(ServerConsoleText));
        }
    }

    private void AddLogLine(string line)
    {
        if (SelectedServer == null) return;
        LogLines.Add(line);
        if (LogLines.Count > 2000)
            LogLines.RemoveAt(0);
        OnPropertyChanged(nameof(ServerConsoleText));
        CommandManager.InvalidateRequerySuggested();
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
                MaxRamMB,
                onlineMode: NewServerOnlineMode);

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

        var result = DialogHelper.Confirm(
            $"¿Eliminar el servidor '{server.Name}'? Se borrará su carpeta y todos sus datos.",
            "Eliminar servidor");
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
        OnPropertyChanged(nameof(ServerConsoleText));
        await _serverService.SendCommandAsync(SelectedServer, command);
        CommandText = string.Empty;
    }

    private async Task ToggleServer(object? param)
    {
        var server = param as MinecraftServer ?? SelectedServer;
        if (server == null) return;

        if (server.Status == ServerStatus.Running || server.Status == ServerStatus.Starting)
            await StopServer(server);
        else
            await StartServer(server);

        OnPropertyChanged(nameof(ServerActionLabel));
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
        OnPropertyChanged(nameof(ServerConsoleText));
        StatusMessage = "Consola limpiada.";
        CommandManager.InvalidateRequerySuggested();
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
