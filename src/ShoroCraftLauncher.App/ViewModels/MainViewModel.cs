using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using ShoroCraftLauncher.App.Commands;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.App.ViewModels;

public class MainViewModel : BaseViewModel
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILauncherService _launcherService;
    private readonly IAuthenticationService _authService;
    private readonly IProfileService _profileService;
    private readonly IMinecraftService _minecraftService;

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

    private string _authStatus = "No autenticado";
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
        set => SetProperty(ref _isGameRunning, value);
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

    private string _selectedProfileName = "Sin perfil";
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
            if (value != null)
            {
                SelectedProfileName = value.Name;
                GameVersionStatus = $"{value.MinecraftVersion} | {value.Type}";
            }
        }
    }

    private AuthResult? _currentAuth;
    public ICommand NavigateCommand { get; }
    public ICommand LaunchGameCommand { get; }
    public ICommand StopGameCommand { get; }
    public ICommand LoginCommand { get; }

    public MainViewModel(
        IServiceProvider serviceProvider,
        ILauncherService launcherService,
        IAuthenticationService authService,
        IProfileService profileService,
        IMinecraftService minecraftService)
    {
        _serviceProvider = serviceProvider;
        _launcherService = launcherService;
        _authService = authService;
        _profileService = profileService;
        _minecraftService = minecraftService;

        _profileService.SelectedProfileChanged += () =>
        {
            OnPropertyChanged(nameof(SelectedProfile));
            if (SelectedProfile != null)
            {
                SelectedProfileName = SelectedProfile.Name;
                GameVersionStatus = $"{SelectedProfile.MinecraftVersion} | {SelectedProfile.Type}";
            }
        };

        NavItems.Add(new NavItem { Name = "Dashboard", Icon = "🏠" });
        NavItems.Add(new NavItem { Name = "Perfiles", Icon = "👤" });
        NavItems.Add(new NavItem { Name = "Mods", Icon = "🔧" });
        NavItems.Add(new NavItem { Name = "Texturas", Icon = "🎨" });
        NavItems.Add(new NavItem { Name = "Shaders", Icon = "✨" });
        NavItems.Add(new NavItem { Name = "Scripts", Icon = "📜" });
        NavItems.Add(new NavItem { Name = "Consola", Icon = "🖥️" });
        NavItems.Add(new NavItem { Name = "Configuración", Icon = "⚙️" });

        NavigateCommand = new RelayCommand(p => SelectedNav = p?.ToString() ?? "Dashboard");
        LaunchGameCommand = new RelayCommand(async _ => await LaunchGame(), _ => SelectedProfile != null && !IsGameRunning);
        StopGameCommand = new RelayCommand(async _ => await StopGame(), _ => IsGameRunning);
        LoginCommand = new RelayCommand(async _ => await Login());

        _launcherService.GameExited += () =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                IsGameRunning = false;
                StatusMessage = "Juego cerrado.";
                IsDownloading = false;
            });
        };

        _launcherService.ProgressChanged += (pct, msg) =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (pct >= 0)
                {
                    IsDownloading = true;
                    DownloadProgress = pct;
                }
                DownloadStatus = msg;
                StatusMessage = msg;
            });
        };

        _ = LoadInitialDataAsync();
    }

    private async Task LoadInitialDataAsync()
    {
        await _profileService.LoadProfilesAsync();
        SelectedNav = "Dashboard";
    }

    private void NavigateTo(string navName)
    {
        BaseViewModel? vm = navName switch
        {
            "Dashboard" => _serviceProvider.GetRequiredService<DashboardViewModel>(),
            "Perfiles" => _serviceProvider.GetRequiredService<ProfilesViewModel>(),
            "Mods" => _serviceProvider.GetRequiredService<ModsViewModel>(),
            "Texturas" => _serviceProvider.GetRequiredService<ResourcePacksViewModel>(),
            "Shaders" => _serviceProvider.GetRequiredService<ShaderPacksViewModel>(),
            "Scripts" => _serviceProvider.GetRequiredService<ScriptsViewModel>(),
            "Consola" => _serviceProvider.GetRequiredService<ConsoleViewModel>(),
            "Configuración" => _serviceProvider.GetRequiredService<SettingsViewModel>(),
            _ => null
        };

        if (vm is DashboardViewModel dash)
            _ = dash.LoadDataAsync();

        CurrentView = vm;
    }

    private async Task LaunchGame()
    {
        if (SelectedProfile == null || IsGameRunning) return;

        if (_currentAuth == null || !_currentAuth.Success || _currentAuth.Username != Username)
        {
            _currentAuth = await _authService.AuthenticateOfflineAsync(Username);
            if (!_currentAuth.Success)
            {
                StatusMessage = "Error: No hay autenticación disponible.";
                return;
            }
            IsAuthenticated = true;
            AuthStatus = $"Jugando como: {_currentAuth.Username}";
        }

        IsBusy = true;
        StatusMessage = $"Iniciando {SelectedProfileName}...";
        SelectedNav = "Consola";

        var result = await _launcherService.LaunchProfileAsync(SelectedProfile, _currentAuth);

        if (result.Success)
        {
            IsGameRunning = true;
            StatusMessage = $"Juego iniciado (PID: {result.ProcessId})";
        }
        else
        {
            StatusMessage = $"Error: {result.ErrorMessage}";
        }

        IsBusy = false;
    }

    private async Task StopGame()
    {
        await _launcherService.StopGameAsync();
        IsGameRunning = false;
        StatusMessage = "Juego detenido.";
    }

    private async Task Login()
    {
        IsBusy = true;
        StatusMessage = "Autenticando offline...";
        await Task.Delay(300);

        _currentAuth = await _authService.AuthenticateOfflineAsync(Username);
        if (_currentAuth.Success)
        {
            IsAuthenticated = true;
            AuthStatus = $"Jugando como: {_currentAuth.Username}";
            SkinUrl = _currentAuth.SkinUrl;
            StatusMessage = "Autenticación offline exitosa.";
        }
        else
        {
            StatusMessage = _currentAuth.ErrorMessage ?? "Error de autenticación.";
        }
        IsBusy = false;
    }
}

public class NavItem
{
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
}
