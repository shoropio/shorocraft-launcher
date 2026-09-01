using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.App.Commands;
using ShoroCraftLauncher.App.Models;
using ShoroCraftLauncher.App.Services;
using ShoroCraftLauncher.Core;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.App.ViewModels;


public class StatCard : BaseViewModel
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public System.Windows.Media.Brush BarBrush { get; set; } = System.Windows.Media.Brushes.Transparent;
}


public partial class DashboardViewModel : BaseViewModel, IDisposable
{
    private readonly IProfileService _profileService;
    private readonly IGameVersionRepository _versionRepo;
    private readonly IMinecraftService _minecraftService;
    private readonly ILauncherService _launcherService;
    private readonly IJavaService _javaService;
    private readonly IUpdaterService _updaterService;
    private readonly IModService _modService;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IProfileRepository _profileRepo;
    private readonly INewsService _newsService;
    private readonly IControllerDetectionService _controllerDetection;
    private readonly IToastService _toastService;
    private readonly ILogger<DashboardViewModel> _logger;

    private const string LastNotifiedVersionKey = "last_notified_minecraft_version";

    public ObservableCollection<Profile> Profiles => _profileService.Profiles;
    public ObservableCollection<GameVersion> AvailableVersions { get; } = new();
    public ObservableCollection<StatCard> StatCards { get; } = new();
    public ObservableCollection<NewsItem> NewsItems { get; } = new();

    public Profile? SelectedProfile
    {
        get => _profileService.SelectedProfile;
        set 
        {
            _profileService.SelectedProfile = value;
            OnPropertyChanged(nameof(SelectedProfile));
            _ = UpdateProfileDetailsAsync();
        }
    }

    private string _installedVersion = "No instalado";
    public string InstalledVersion
    {
        get => _installedVersion;
        set => SetProperty(ref _installedVersion, value);
    }

    private string _modsCount = "0 mods";
    public string ModsCount
    {
        get => _modsCount;
        set => SetProperty(ref _modsCount, value);
    }

    private string _allocatedRam = "4 GB";
    public string AllocatedRam
    {
        get => _allocatedRam;
        set => SetProperty(ref _allocatedRam, value);
    }

    private string _readyStatus = "Listo";
    public string ReadyStatus
    {
        get => _readyStatus;
        set => SetProperty(ref _readyStatus, value);
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

    private string _selectedVersion = "latest";
    public string SelectedVersion
    {
        get => _selectedVersion;
        set => SetProperty(ref _selectedVersion, value);
    }

    private bool _hasUpdateNotification;
    public bool HasUpdateNotification
    {
        get => _hasUpdateNotification;
        set => SetProperty(ref _hasUpdateNotification, value);
    }

    private string _updateNotificationMessage = string.Empty;
    public string UpdateNotificationMessage
    {
        get => _updateNotificationMessage;
        set => SetProperty(ref _updateNotificationMessage, value);
    }

    private bool _hasLauncherUpdate;
    public bool HasLauncherUpdate
    {
        get => _hasLauncherUpdate;
        set => SetProperty(ref _hasLauncherUpdate, value);
    }

    private string _launcherUpdateMessage = string.Empty;
    public string LauncherUpdateMessage
    {
        get => _launcherUpdateMessage;
        set => SetProperty(ref _launcherUpdateMessage, value);
    }

    private string? _launcherUpdateUrl;
    private string? _launcherUpdateSha256;
    private string? _latestVersion;
    private bool _isInstallingUpdate;
    public bool IsInstallingUpdate
    {
        get => _isInstallingUpdate;
        set => SetProperty(ref _isInstallingUpdate, value);
    }

    private bool _isIrisSodiumInstalled;
    public bool IsIrisSodiumInstalled
    {
        get => _isIrisSodiumInstalled;
        set
        {
            if (SetProperty(ref _isIrisSodiumInstalled, value))
            {
                OnPropertyChanged(nameof(IrisSodiumButtonText));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string IrisSodiumButtonText =>
        IsIrisSodiumInstalled ? "Iris + Sodium instalado" : "Instalar Iris + Sodium (Fabric)";

    private bool _isControllerConnected;
    public bool IsControllerConnected
    {
        get => _isControllerConnected;
        set => SetProperty(ref _isControllerConnected, value);
    }

    private bool _isControllerModInstalled;
    public bool IsControllerModInstalled
    {
        get => _isControllerModInstalled;
        set
        {
            if (SetProperty(ref _isControllerModInstalled, value))
            {
                OnPropertyChanged(nameof(ShowControllerRecommendation));
                OnPropertyChanged(nameof(ControllerSupportButtonText));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool ShowControllerRecommendation => IsControllerConnected && !IsControllerModInstalled;

    public string ControllerSupportButtonText =>
        IsControllerModInstalled ? "Soporte de mando instalado" : "Instalar soporte de mando (Controlify)";

    private bool _isJavaReady;
    public bool IsJavaReady
    {
        get => _isJavaReady;
        set => SetProperty(ref _isJavaReady, value);
    }

    private bool _isVersionReady;
    public bool IsVersionReady
    {
        get => _isVersionReady;
        set => SetProperty(ref _isVersionReady, value);
    }

    private bool _isLoaderReady;
    public bool IsLoaderReady
    {
        get => _isLoaderReady;
        set => SetProperty(ref _isLoaderReady, value);
    }

    private string? _loaderUpdateVersion;
    public string? LoaderUpdateVersion
    {
        get => _loaderUpdateVersion;
        set => SetProperty(ref _loaderUpdateVersion, value);
    }

        private bool _hasLoaderUpdate;
    public bool HasLoaderUpdate
    {
        get => _hasLoaderUpdate;
        set => SetProperty(ref _hasLoaderUpdate, value);
    }

    private string? _loaderUpdateCacheKey;
    private string? _loaderUpdateCacheVersion;
    private readonly System.Threading.SemaphoreSlim _loaderUpdateLock = new(1, 1);
    private string? _loaderToastKey;
    private readonly object _loaderToastLock = new();

    private string _loaderUpdateMessage = string.Empty;
    public string LoaderUpdateMessage
    {
        get => _loaderUpdateMessage;
        set => SetProperty(ref _loaderUpdateMessage, value);
    }

    private bool _isRamReady;
    public bool IsRamReady
    {
        get => _isRamReady;
        set => SetProperty(ref _isRamReady, value);
    }

    private string _checklistMessage = "Verificando...";
    public string ChecklistMessage
    {
        get => _checklistMessage;
        set => SetProperty(ref _checklistMessage, value);
    }

    public ICommand RefreshVersionsCommand { get; }
    public ICommand InstallVersionCommand { get; }
    public ICommand InstallLoaderCommand { get; }
    public ICommand ApplyProfileCommand { get; }
    public ICommand DownloadLauncherUpdateCommand { get; }
    public ICommand InstallIrisCommand { get; }
    public ICommand InstallControllerSupportCommand { get; }
    public ICommand OptiFineInfoCommand { get; }
    public ICommand RepairProfileCommand { get; }
    public ICommand InstallMinecraftUpdateCommand { get; }
    public ICommand DismissMinecraftUpdateCommand { get; }
    public ICommand InstallFabricIrisSodiumCommand { get; }
    public ICommand UpdateLoaderCommand { get; }

    private string? _latestAvailableVersion;

    public DashboardViewModel(
        IProfileService profileService,
        IGameVersionRepository versionRepo,
        IMinecraftService minecraftService,
        ILauncherService launcherService,
        IJavaService javaService,
        IUpdaterService updaterService,
        IModService modService,
        ISettingsRepository settingsRepo,
        IProfileRepository profileRepo,
        INewsService newsService,
        IControllerDetectionService controllerDetection,
        IToastService toastService,
        ILogger<DashboardViewModel> logger)
    {
        _profileService = profileService;
        _versionRepo = versionRepo;
        _minecraftService = minecraftService;
        _launcherService = launcherService;
        _javaService = javaService;
        _updaterService = updaterService;
        _modService = modService;
        _settingsRepo = settingsRepo;
        _profileRepo = profileRepo;
        _newsService = newsService;
        _controllerDetection = controllerDetection;
        _toastService = toastService;
        _logger = logger;

        _profileService.SelectedProfileChanged += OnSelectedProfileChanged;

        RefreshVersionsCommand = new RelayCommand(async _ => await LoadVersionsAsync());
        InstallVersionCommand = new RelayCommand(async p => await InstallVersion(p?.ToString() ?? "latest"), _ => !IsDownloading && !IsBusy);
        InstallLoaderCommand = new RelayCommand(async p => await InstallLoader(p?.ToString() ?? ""), _ => !IsDownloading && !IsBusy && SelectedProfile != null);
        ApplyProfileCommand = new RelayCommand(async _ => await ApplyProfile(), _ => !IsBusy);
        DownloadLauncherUpdateCommand = new RelayCommand(async _ => await InstallLauncherUpdateAsync(), _ => !IsBusy);
        
        InstallIrisCommand = new RelayCommand(async _ => await InstallIris(), _ => SelectedProfile != null && SelectedProfile.Type == ShoroCraftLauncher.Core.Enums.ProfileType.Fabric);
        InstallControllerSupportCommand = new RelayCommand(async _ => await InstallControllerSupportAsync(), _ => SelectedProfile != null && !IsDownloading && ShowControllerRecommendation);
        OptiFineInfoCommand = new RelayCommand(_ => 
        {
            DialogHelper.Show("OptiFine no permite descargas automáticas.\n\nSe abrirá la página oficial. Descarga la versión correspondiente a tu juego, ve a la pestaña de 'Mods' en el Launcher y arrastra el archivo .jar descargado para instalarlo.", "OptiFine", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://optifine.net/downloads") { UseShellExecute = true });
        });
        RepairProfileCommand = new RelayCommand(async _ => await RepairProfile(), _ => SelectedProfile != null && !IsBusy && !IsDownloading);
        InstallMinecraftUpdateCommand = new RelayCommand(async _ => await InstallMinecraftUpdateAsync());
        DismissMinecraftUpdateCommand = new RelayCommand(async _ => await DismissMinecraftUpdateAsync());
        InstallFabricIrisSodiumCommand = new RelayCommand(async _ => await InstallFabricIrisSodium(), _ => !IsDownloading && !IsBusy && !IsIrisSodiumInstalled);
        UpdateLoaderCommand = new RelayCommand(async _ => await UpdateLoader(), _ => !IsDownloading && !IsBusy && HasLoaderUpdate && SelectedProfile != null);

        InitializeStatCards();
    }

    private void InitializeStatCards()
    {
        StatCards.Clear();
        StatCards.Add(new StatCard
        {
            Label = TryGetString("Dash_Version") ?? "Versión",
            Value = InstalledVersion,
            BarBrush = TryGetBrush("PrimaryGradient") ?? System.Windows.Media.Brushes.Transparent
        });
        StatCards.Add(new StatCard
        {
            Label = TryGetString("Dash_Mods") ?? "Mods",
            Value = ModsCount,
            BarBrush = TryGetBrush("AccentBrush") ?? System.Windows.Media.Brushes.Transparent
        });
        StatCards.Add(new StatCard
        {
            Label = TryGetString("Dash_Ram") ?? "RAM",
            Value = AllocatedRam,
            BarBrush = TryGetBrush("SuccessBrush") ?? System.Windows.Media.Brushes.Transparent
        });
        StatCards.Add(new StatCard
        {
            Label = TryGetString("Dash_Status") ?? "Estado",
            Value = ReadyStatus,
            BarBrush = TryGetBrush("SuccessBrush") ?? System.Windows.Media.Brushes.Transparent
        });
    }

    private void UpdateStatCards()
    {
        if (StatCards.Count >= 4)
        {
            StatCards[0].Value = InstalledVersion;
            StatCards[1].Value = ModsCount;
            StatCards[2].Value = AllocatedRam;
            StatCards[3].Value = ReadyStatus;
        }
    }

    private static System.Windows.Media.Brush? TryGetBrush(string key)
    {
        var app = System.Windows.Application.Current;
        if (app?.TryFindResource(key) is System.Windows.Media.Brush brush)
            return brush;
        return null;
    }

    private static string? TryGetString(string key)
    {
        var app = System.Windows.Application.Current;
        if (app?.TryFindResource(key) is string value)
            return value;
        return null;
    }

    private void OnSelectedProfileChanged()
    {
        OnPropertyChanged(nameof(SelectedProfile));
        _ = UpdateProfileDetailsAsync();
        _ = RefreshControllerSupportStateAsync();
    }

    public void Dispose()
    {
        _profileService.SelectedProfileChanged -= OnSelectedProfileChanged;
        GC.SuppressFinalize(this);
    }

    public async Task LoadDataAsync()
    {
        IsBusy = true;
        try
        {
            await _profileService.LoadProfilesAsync();
            if (SelectedProfile != null)
            {
                await _profileService.SyncProfileFilesAsync(SelectedProfile);
            }

            var currentVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";
            var versionsTask = LoadVersionsAsync();
            var detailsTask = UpdateProfileDetailsAsync();
            var componentsTask = UpdateComponentInstallStatesAsync();
            var controllerTask = RefreshControllerSupportStateAsync();
            var updateTask = _updaterService.CheckForUpdatesAsync(currentVersion);
            var newsTask = LoadNewsAsync();

            await Task.WhenAll(versionsTask, detailsTask, componentsTask, controllerTask, updateTask, newsTask);

            var (isUpdateAvailable, latestVersion, downloadUrl, sha256) = updateTask.Result;
            if (isUpdateAvailable)
            {
                HasLauncherUpdate = true;
                _latestVersion = latestVersion;
                LauncherUpdateMessage = $"¡ShoroCraft Launcher {latestVersion} disponible!";
                _launcherUpdateUrl = downloadUrl;
                _launcherUpdateSha256 = sha256;
            }

            ReadyStatus = "Listo";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dashboard load failed");
            ReadyStatus = "Error";
            StatusMessage = "Error al cargar dashboard.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadNewsAsync()
    {
        try
        {
            var news = await _newsService.GetNewsAsync();
            NewsItems.Clear();
            foreach (var item in news)
                NewsItems.Add(item);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load news feed");
        }
    }

    private async Task InstallLauncherUpdateAsync()
    {
        if (string.IsNullOrEmpty(_launcherUpdateUrl))
        {
            DialogHelper.Show("No se encontró una actualización disponible para descargar.",
                "Actualizar Launcher", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        IsInstallingUpdate = true;
        try
        {
            var installerPath = await _updaterService.DownloadUpdateAsync(_launcherUpdateUrl, _latestVersion ?? "latest", _launcherUpdateSha256);
            if (installerPath == null)
            {
                DialogHelper.Show("No se pudo descargar el instalador. Revisa tu conexión e inténtalo de nuevo.",
                    "Error al actualizar", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            var result = DialogHelper.Show(
                "Se descargó la nueva versión. El instalador se abrirá y el Launcher se cerrará. ¿Continuar?",
                "Actualizar Launcher",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);
            if (result != System.Windows.MessageBoxResult.Yes)
                return;

            await _updaterService.LaunchInstallerAsync(installerPath);
            System.Windows.Application.Current?.Shutdown();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update install failed");
            DialogHelper.Show("Ocurrió un error al instalar la actualización.", "Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
        finally
        {
            IsInstallingUpdate = false;
        }
    }

    private async Task UpdateProfileDetailsAsync()
    {
        try
        {
            if (SelectedProfile != null)
            {
                await _profileService.SyncProfileFilesAsync(SelectedProfile);

                ModsCount = $"Perfil: {SelectedProfile.Name}";
                AllocatedRam = $"{SelectedProfile.MaxRamMB / 1024.0:F0} GB";
                
                var gameDir = GetSelectedProfileGameDirectory();
                var mcPath = new CmlLib.Core.MinecraftPath(gameDir);
                var targetVersion = SelectedProfile.MinecraftVersion;
                
                if (targetVersion.ToLower() == "latest")
                {
                    targetVersion = await _minecraftService.ResolveVersionIdAsync("latest");
                }

                if (SelectedProfile.Type != ShoroCraftLauncher.Core.Enums.ProfileType.Vanilla)
                {
                    var loaderPrefix = SelectedProfile.Type.ToString().ToLower();
                    var dirs = System.IO.Directory.Exists(mcPath.Versions) ? System.IO.Directory.GetDirectories(mcPath.Versions) : Array.Empty<string>();
                    var match = dirs.Select(System.IO.Path.GetFileName)
                        .FirstOrDefault(n => n != null
                            && n.Contains(loaderPrefix, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(
                                n.Split('-', StringSplitOptions.RemoveEmptyEntries).LastOrDefault(),
                                targetVersion,
                                StringComparison.OrdinalIgnoreCase));
                    if (match != null) targetVersion = match;
                }

                var verPath = System.IO.Path.Combine(mcPath.Versions, targetVersion);
                InstalledVersion = System.IO.Directory.Exists(verPath) ? targetVersion : "No instalado";
                
                await ValidateProfileChecklistAsync();
            }
            else
            {
                ModsCount = "0 mods";
                AllocatedRam = "0 GB";
                InstalledVersion = "No instalado";
                
                IsJavaReady = false;
                IsVersionReady = false;
                IsLoaderReady = false;
                IsRamReady = false;
                ChecklistMessage = "Selecciona un perfil.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update selected profile details");
            InstalledVersion = "No instalado";
        }
        finally
        {
            UpdateStatCards();
        }
    }

    private void LogStatus(string message)
    {
        StatusMessage = message;
        _launcherService.Log($"[INFO] {message}");
    }

    private string GetSelectedProfileGameDirectory()
    {
        if (SelectedProfile == null || string.IsNullOrWhiteSpace(SelectedProfile.GameDirectory))
            return _minecraftService.GetDefaultGameDirectory(SelectedProfile?.Name ?? "Default");

        return SelectedProfile.GameDirectory;
    }

    private async Task ApplyProfile()
    {
        if (SelectedProfile != null)
        {
            ReadyStatus = "Listo";
            StatusMessage = $"Perfil \"{SelectedProfile.Name}\" aplicado.";
            await UpdateProfileDetailsAsync();
            await LoadVersionsAsync();
            await UpdateComponentInstallStatesAsync();
        }
    }

}
