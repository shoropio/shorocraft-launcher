using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using ShoroCraftLauncher.App.Commands;
using ShoroCraftLauncher.App.Services;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.App.ViewModels;

public class MainViewModel : BaseViewModel, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILauncherService _launcherService;
    private readonly IAuthenticationService _authService;
    private readonly IProfileService _profileService;
    private readonly IMinecraftService _minecraftService;
    private readonly IModService _modService;
    private readonly Action _selectedProfileChangedHandler;
    private readonly Action<int> _gameExitedHandler;
    private readonly Action<double, string> _progressChangedHandler;
    private readonly Dictionary<string, BaseViewModel> _viewCache = new();

    public ObservableCollection<NavItem> NavItems { get; } = new();
    public ObservableCollection<Profile> Profiles => _profileService.Profiles;

    private BaseViewModel? _currentView;
    public BaseViewModel? CurrentView
    {
        get => _currentView;
        set => SetProperty(ref _currentView, value);
    }

    private string _selectedNav = "Dashboard";
    public string SelectedNav
    {
        get => _selectedNav;
        set
        {
            SetProperty(ref _selectedNav, value);
            NavigateTo(value);
        }
    }

    private string _authStatus = "User_NotAuthenticated";
    public string AuthStatus
    {
        get => _authStatus;
        set => SetProperty(ref _authStatus, value);
    }

    private bool _isAuthenticated;
    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        set => SetProperty(ref _isAuthenticated, value);
    }

    private bool _isGameRunning;
    public bool IsGameRunning
    {
        get => _isGameRunning;
        set
        {
            if (SetProperty(ref _isGameRunning, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    private bool _isSidebarCollapsed;
    public bool IsSidebarCollapsed
    {
        get => _isSidebarCollapsed;
        set => SetProperty(ref _isSidebarCollapsed, value);
    }

    private bool _isDownloading;
    public bool IsDownloading
    {
        get => _isDownloading;
        set
        {
            if (SetProperty(ref _isDownloading, value))
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    private double _downloadProgress;
    public double DownloadProgress
    {
        get => _downloadProgress;
        set => SetProperty(ref _downloadProgress, value);
    }

    private string _downloadStatus = "";
    public string DownloadStatus
    {
        get => _downloadStatus;
        set => SetProperty(ref _downloadStatus, value);
    }

    private string _gameVersionStatus = "No instalado";
    public string GameVersionStatus
    {
        get => _gameVersionStatus;
        set => SetProperty(ref _gameVersionStatus, value);
    }

    private string _username = Environment.UserName;
    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    private string _selectedProfileName = "TopBar_NoProfile";
    public string SelectedProfileName
    {
        get => _selectedProfileName;
        set => SetProperty(ref _selectedProfileName, value);
    }

    private string? _skinUrl = "https://crafatar.com/renders/body/8667ba71-b85a-4004-af54-457a9734eed7?overlay=true";
    public string? SkinUrl
    {
        get => _skinUrl;
        set => SetProperty(ref _skinUrl, value);
    }

    public Profile? SelectedProfile
    {
        get => _profileService.SelectedProfile;
        set
        {
            _profileService.SelectedProfile = value;
            OnPropertyChanged(nameof(SelectedProfile));
            CommandManager.InvalidateRequerySuggested();
            if (value != null)
            {
                SelectedProfileName = value.Name;
                _ = UpdateGameVersionStatusAsync(value);
            }
        }
    }

    private async Task UpdateGameVersionStatusAsync(Profile profile)
    {
        try
        {
            var version = profile.MinecraftVersion;
            if (version.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                var resolved = await _minecraftService.ResolveVersionIdAsync("latest");
                GameVersionStatus = $"{resolved} | {profile.Type}";
            }
            else
            {
                GameVersionStatus = $"{version} | {profile.Type}";
            }
        }
        catch
        {
            GameVersionStatus = $"{profile.MinecraftVersion} | {profile.Type}";
        }
    }

    private AuthResult? _currentAuth;
    public ICommand NavigateCommand { get; }
    public ICommand ToggleSidebarCommand { get; }
    public ICommand LaunchGameCommand { get; }
    public ICommand StopGameCommand { get; }
    public ICommand LoginCommand { get; }
    public ICommand LoginOfflineCommand { get; }
    public ICommand LogoutCommand { get; }

    public MainViewModel(
        IServiceProvider serviceProvider,
        ILauncherService launcherService,
        IAuthenticationService authService,
        IProfileService profileService,
        IMinecraftService minecraftService,
        IModService modService)
    {
        _serviceProvider = serviceProvider;
        _launcherService = launcherService;
        _authService = authService;
        _profileService = profileService;
        _minecraftService = minecraftService;
        _modService = modService;

        _selectedProfileChangedHandler = () =>
        {
            OnPropertyChanged(nameof(SelectedProfile));
            CommandManager.InvalidateRequerySuggested();
            if (SelectedProfile != null)
            {
                SelectedProfileName = SelectedProfile.Name;
                GameVersionStatus = $"{SelectedProfile.MinecraftVersion} | {SelectedProfile.Type}";
            }
            else
            {
                SelectedProfileName = "TopBar_NoProfile";
                GameVersionStatus = "No instalado";
            }
        };
        _profileService.SelectedProfileChanged += _selectedProfileChangedHandler;

        NavItems.Add(new NavItem { Name = "Nav_Dashboard", Icon = "🏠" });
        NavItems.Add(new NavItem { Name = "Nav_Profiles", Icon = "👤" });
        NavItems.Add(new NavItem { Name = "Nav_Mods", Icon = "🔧" });
        NavItems.Add(new NavItem { Name = "Nav_ResourcePacks", Icon = "🎨" });
        NavItems.Add(new NavItem { Name = "Nav_ShaderPacks", Icon = "✨" });
        NavItems.Add(new NavItem { Name = "Nav_Scripts", Icon = "📜" });
        NavItems.Add(new NavItem { Name = "Nav_Maps", Icon = "🗺️" });
        NavItems.Add(new NavItem { Name = "Nav_Servers", Icon = "🛡️" });
        NavItems.Add(new NavItem { Name = "Nav_Console", Icon = "🖥️" });
        NavItems.Add(new NavItem { Name = "Nav_Settings", Icon = "⚙️" });

        NavigateCommand = new RelayCommand(p => SelectedNav = p?.ToString() ?? "Nav_Dashboard");
        ToggleSidebarCommand = new RelayCommand(_ => IsSidebarCollapsed = !IsSidebarCollapsed);
        LaunchGameCommand = new RelayCommand(async _ => await LaunchGame(), _ => SelectedProfile != null && !IsGameRunning && !IsBusy);
        StopGameCommand = new RelayCommand(async _ => await StopGame(), _ => IsGameRunning && !IsBusy);
        LoginCommand = new RelayCommand(async _ => await LoginMicrosoft());
        LoginOfflineCommand = new RelayCommand(async _ => await LoginOffline());
        LogoutCommand = new RelayCommand(async _ => await Logout());

        _gameExitedHandler = (exitCode) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                IsGameRunning = false;
                IsDownloading = false;
                DownloadProgress = 0;
                DownloadStatus = string.Empty;

                if (exitCode == 0)
                {
                    StatusMessage = "Juego cerrado.";
                }
                else
                {
                    var recentLogs = _launcherService.LogHistory
                        .Where(l => l.Contains("[ERROR]") || l.Contains("crashed", StringComparison.OrdinalIgnoreCase) || l.Contains("Mixin", StringComparison.OrdinalIgnoreCase))
                        .TakeLast(5)
                        .ToList();
                    var hint = recentLogs.Count > 0
                        ? "\nRevisa la consola para más detalles."
                        : "";
                    StatusMessage = $"Juego terminó con error (código {exitCode}).{hint}";
                }
            });
        };
        _launcherService.GameExited += _gameExitedHandler;

        _progressChangedHandler = (pct, msg) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                IsDownloading = true;
                if (pct >= 0)
                {
                    DownloadProgress = pct;
                }
                DownloadStatus = msg;
                StatusMessage = msg;
            });
        };
        _launcherService.ProgressChanged += _progressChangedHandler;

        _ = LoadInitialDataAsync();
    }

    private async Task LoadInitialDataAsync()
    {
        try
        {
            await _profileService.LoadProfilesAsync();
            await TryRestoreSessionAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"Failed to load initial data: {ex}");
        }
        finally
        {
            SelectedNav = "Nav_Dashboard";
        }
    }

    private async Task TryRestoreSessionAsync()
    {
        try
        {
            _currentAuth = await _authService.AuthenticateSilentlyAsync();
            if (_currentAuth.Success)
            {
                Username = _currentAuth.Username ?? Username;
                IsAuthenticated = true;
                AuthStatus = $"Microsoft: {_currentAuth.Username}";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"Failed to restore session: {ex.Message}");
        }
    }

    private void NavigateTo(string navName)
    {
        if (!_viewCache.TryGetValue(navName, out BaseViewModel? vm))
        {
            vm = navName switch
            {
                "Nav_Dashboard" => _serviceProvider.GetRequiredService<DashboardViewModel>(),
                "Nav_Profiles" => _serviceProvider.GetRequiredService<ProfilesViewModel>(),
                "Nav_Mods" => _serviceProvider.GetRequiredService<ModsViewModel>(),
                "Nav_ResourcePacks" => _serviceProvider.GetRequiredService<ResourcePacksViewModel>(),
                "Nav_ShaderPacks" => _serviceProvider.GetRequiredService<ShaderPacksViewModel>(),
                "Nav_Scripts" => _serviceProvider.GetRequiredService<ScriptsViewModel>(),
                "Nav_Maps" => _serviceProvider.GetRequiredService<MapsViewModel>(),
                "Nav_Servers" => _serviceProvider.GetRequiredService<ServersViewModel>(),
                "Nav_Console" => _serviceProvider.GetRequiredService<ConsoleViewModel>(),
                "Nav_Settings" => _serviceProvider.GetRequiredService<SettingsViewModel>(),
                _ => null
            };

            if (vm != null)
                _viewCache[navName] = vm;
        }

        if (!ReferenceEquals(CurrentView, vm) && CurrentView is IDisposable disposable)
            disposable.Dispose();

        CurrentView = vm;

        if (vm is DashboardViewModel dash)
            _ = dash.LoadDataAsync();
    }

    private async Task LaunchGame()
    {
        if (SelectedProfile == null || IsGameRunning) return;

        if (_currentAuth == null || !_currentAuth.Success || (_currentAuth.IsOffline && _currentAuth.Username != Username))
        {
            _currentAuth = await _authService.AuthenticateOfflineAsync(Username);
            if (!_currentAuth.Success)
            {
                StatusMessage = "Error: No hay autenticación disponible.";
                return;
            }
            IsAuthenticated = true;
            AuthStatus = $"Offline: {_currentAuth.Username}";
        }

        IsBusy = true;
        try
        {
            StatusMessage = $"Iniciando {SelectedProfileName}...";
            SelectedNav = "Nav_Console";

            // Check mod compatibility before launch
            var targetMcVersion = SelectedProfile.MinecraftVersion;
            if (targetMcVersion.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                targetMcVersion = await _minecraftService.ResolveVersionIdAsync("latest");
            }
            
            var compatResult = await _modService.CheckAndDisableIncompatibleModsAsync(SelectedProfile.Id, targetMcVersion);
            if (compatResult.Disabled.Count > 0)
            {
                var disabledList = string.Join(", ", compatResult.Disabled);
                StatusMessage = $"Se deshabilitaron {compatResult.Disabled.Count} mods incompatibles: {disabledList}";
                _launcherService.Log($"[WARN] Mods incompatibles deshabilitados: {disabledList}");
            }
            else if (compatResult.Checked > 0)
            {
                StatusMessage = $"Compatibilidad verificada: {compatResult.Checked} mods OK";
            }

            var result = await _launcherService.LaunchProfileAsync(SelectedProfile, _currentAuth);

            if (result.Success)
            {
                IsGameRunning = true;
                StatusMessage = $"Juego iniciado (PID: {result.ProcessId})";
                IsDownloading = false;
                DownloadProgress = 0;
                DownloadStatus = string.Empty;
            }
            else
            {
                StatusMessage = $"Error: {result.ErrorMessage}";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StopGame()
    {
        await _launcherService.StopGameAsync();
        IsGameRunning = false;
        StatusMessage = "Juego detenido.";
    }

    private async Task LoginMicrosoft()
    {
        IsBusy = true;
        try
        {
            StatusMessage = "Abriendo autenticación Microsoft...";

            _currentAuth = await _authService.AuthenticateAsync();
            if (_currentAuth.Success)
            {
                Username = _currentAuth.Username ?? Username;
                IsAuthenticated = true;
                AuthStatus = $"Microsoft: {_currentAuth.Username}";
                SkinUrl = _currentAuth.SkinUrl;
                StatusMessage = "Autenticación Microsoft exitosa.";
            }
            else
            {
                StatusMessage = _currentAuth.ErrorMessage ?? "Error de autenticación Microsoft.";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoginOffline()
    {
        IsBusy = true;
        try
        {
            StatusMessage = "Autenticando offline...";
            await Task.Delay(300);

            _currentAuth = await _authService.AuthenticateOfflineAsync(Username);
            if (_currentAuth.Success)
            {
                IsAuthenticated = true;
                AuthStatus = $"Offline: {_currentAuth.Username}";
                SkinUrl = _currentAuth.SkinUrl;
                StatusMessage = "Autenticación offline exitosa.";
            }
            else
            {
                StatusMessage = _currentAuth.ErrorMessage ?? "Error de autenticación.";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task Logout()
    {
        var result = DialogHelper.Confirm(
            "¿Cerrar sesión? Se eliminarán las credenciales guardadas y volverás al modo offline.",
            "Cerrar sesión");
        if (result != System.Windows.MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            await _authService.LogoutAsync();
            _currentAuth = null;
            IsAuthenticated = false;
            AuthStatus = "User_NotAuthenticated";
            Username = Environment.UserName;
            SkinUrl = null;
            StatusMessage = "Sesión cerrada.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dispose()
    {
        _profileService.SelectedProfileChanged -= _selectedProfileChangedHandler;
        _launcherService.GameExited -= _gameExitedHandler;
        _launcherService.ProgressChanged -= _progressChangedHandler;

        foreach (var view in _viewCache.Values)
        {
            if (view is IDisposable disposable)
                disposable.Dispose();
        }
        _viewCache.Clear();

        GC.SuppressFinalize(this);
    }
}

public class NavItem
{
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
}
