using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.App.Commands;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.App.ViewModels;

public class DashboardViewModel : BaseViewModel, IDisposable
{
    private readonly IProfileService _profileService;
    private readonly IGameVersionRepository _versionRepo;
    private readonly IMinecraftService _minecraftService;
    private readonly ILauncherService _launcherService;
    private readonly IJavaService _javaService;
    private readonly ILogger<DashboardViewModel> _logger;

    public ObservableCollection<Profile> Profiles => _profileService.Profiles;
    public ObservableCollection<GameVersion> AvailableVersions { get; } = new();

    public Profile? SelectedProfile
    {
        get => _profileService.SelectedProfile;
        set 
        {
            _profileService.SelectedProfile = value;
            OnPropertyChanged(nameof(SelectedProfile));
            UpdateProfileDetailsAsync();
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

    public ICommand RefreshVersionsCommand { get; }
    public ICommand InstallVersionCommand { get; }
    public ICommand InstallLoaderCommand { get; }
    public ICommand ApplyProfileCommand { get; }

    public DashboardViewModel(
        IProfileService profileService,
        IGameVersionRepository versionRepo,
        IMinecraftService minecraftService,
        ILauncherService launcherService,
        IJavaService javaService,
        ILogger<DashboardViewModel> logger)
    {
        _profileService = profileService;
        _versionRepo = versionRepo;
        _minecraftService = minecraftService;
        _launcherService = launcherService;
        _javaService = javaService;
        _logger = logger;

        _profileService.SelectedProfileChanged += OnSelectedProfileChanged;

        RefreshVersionsCommand = new RelayCommand(async _ => await LoadVersionsAsync());
        InstallVersionCommand = new RelayCommand(async p => await InstallVersion(p?.ToString() ?? "latest"));
        InstallLoaderCommand = new RelayCommand(async p => await InstallLoader(p?.ToString() ?? ""));
        ApplyProfileCommand = new RelayCommand(_ => ApplyProfile());
    }

    private void OnSelectedProfileChanged()
    {
        OnPropertyChanged(nameof(SelectedProfile));
        UpdateProfileDetailsAsync();
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
            await LoadVersionsAsync();
            UpdateProfileDetailsAsync();

            ReadyStatus = "Listo";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dashboard load failed");
            ReadyStatus = "Error";
            StatusMessage = "Error al cargar dashboard.";
        }
        IsBusy = false;
    }

    private async void UpdateProfileDetailsAsync()
    {
        if (SelectedProfile != null)
        {
            ModsCount = $"Perfil: {SelectedProfile.Name}";
            AllocatedRam = $"{SelectedProfile.MaxRamMB / 1024.0:F0} GB";
            
            var mcPath = new CmlLib.Core.MinecraftPath();
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
                                .FirstOrDefault(n => n != null && n.Contains(loaderPrefix) && n.Contains(targetVersion));
                if (match != null) targetVersion = match;
            }

            var verPath = System.IO.Path.Combine(mcPath.Versions, targetVersion);
            InstalledVersion = System.IO.Directory.Exists(verPath) ? targetVersion : "No instalado";
        }
        else
        {
            ModsCount = "0 mods";
            AllocatedRam = "0 GB";
            InstalledVersion = "No instalado";
        }
    }

    private void LogStatus(string message)
    {
        StatusMessage = message;
        _launcherService.Log($"[INFO] {message}");
    }

    private async Task LoadVersionsAsync()
    {
        IsBusy = true;
        ReadyStatus = "Obteniendo versiones...";
        LogStatus("Obteniendo versiones estables de Minecraft...");
        try
        {
            var versions = await _minecraftService.FetchAvailableVersionsAsync();
            var stableVersions = versions
                .Where(v => v.VersionType.Equals("release", StringComparison.OrdinalIgnoreCase))
                .Take(50)
                .ToList();

            AvailableVersions.Clear();
            foreach (var v in stableVersions)
                AvailableVersions.Add(v);

            if (stableVersions.Count > 0 && (string.IsNullOrWhiteSpace(SelectedVersion) || SelectedVersion == "latest"))
                SelectedVersion = stableVersions[0].VersionId;

            ReadyStatus = "Listo";
            LogStatus($"{stableVersions.Count} versiones estables disponibles. Más nueva estable: {SelectedVersion}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch versions");
            ReadyStatus = "Error";
            StatusMessage = "Error al obtener versiones.";
            _launcherService.Log($"[ERROR] Error al obtener versiones: {ex.Message}");
        }
        IsBusy = false;
    }

    private void ApplyProfile()
    {
        if (SelectedProfile != null)
        {
            ReadyStatus = "Listo";
            StatusMessage = $"Perfil \"{SelectedProfile.Name}\" aplicado.";
            UpdateProfileDetailsAsync();
        }
    }

    private async Task InstallVersion(string? versionId)
    {
        if (string.IsNullOrEmpty(versionId)) return;
        IsDownloading = true;
        DownloadProgress = 0;
        ReadyStatus = $"Instalando {versionId}...";
        StatusMessage = $"Instalando Minecraft {versionId}...";

        try
        {
            if (versionId.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                LogStatus("Resolviendo la versión estable más nueva de Minecraft...");
                versionId = await _minecraftService.ResolveVersionIdAsync("latest");
                ReadyStatus = $"Instalando {versionId}...";
            }

            LogStatus($"Instalando Minecraft {versionId}...");
            var progress = new Progress<double>(p => DownloadProgress = p);
            await _minecraftService.InstallVersionAsync(versionId, progress);
            InstalledVersion = versionId;
            ReadyStatus = "Listo";
            LogStatus($"Minecraft {versionId} instalado.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Install failed");
            ReadyStatus = "Error";
            StatusMessage = $"Error: {ex.Message}";
            _launcherService.Log($"[ERROR] Error instalando Minecraft {versionId}: {ex.Message}");
        }
        IsDownloading = false;
    }

    private async Task InstallLoader(string? loaderArg)
    {
        if (string.IsNullOrEmpty(loaderArg) || SelectedProfile == null) return;
        var parts = loaderArg.Split(':');
        if (parts.Length < 2) return;

        var loaderType = parts[0];
        var loaderVersion = parts[1];

        IsDownloading = true;
        ReadyStatus = $"Preparando {loaderType}...";
        _launcherService.Log($"[INFO] Preparando instalación de {loaderType}...");
        StatusMessage = $"Preparando instalación de {loaderType}...";
        try
        {
            var mcVersion = SelectedProfile.MinecraftVersion;
            if (mcVersion.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                LogStatus("Resolviendo la versión estable más nueva de Minecraft...");
                mcVersion = await _minecraftService.ResolveVersionIdAsync("latest");
            }

            ReadyStatus = $"Instalando {loaderType}...";

            if (loaderVersion.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = $"Obteniendo última versión de {loaderType}...";
                _launcherService.Log($"[INFO] Obteniendo la versión estable más nueva de {loaderType} para Minecraft {mcVersion}...");
                var resolved = await _minecraftService.ResolveLatestLoaderVersionAsync(loaderType, mcVersion);
                if (resolved.Equals("latest", StringComparison.OrdinalIgnoreCase))
                    throw new Exception($"No se pudo determinar la última versión de {loaderType} para Minecraft {mcVersion}. Es posible que {loaderType} no tenga soporte para esa versión.");
                loaderVersion = resolved;
                StatusMessage = $"{loaderType} {loaderVersion} encontrado.";
                _launcherService.Log($"[INFO] {loaderType} {loaderVersion} encontrado.");
            }

            var javaPath = SelectedProfile.JavaPath;
            if (string.IsNullOrEmpty(javaPath))
            {
                LogStatus($"Buscando Java recomendado para Minecraft {mcVersion}...");
                javaPath = await _javaService.GetRecommendedJavaPathAsync(mcVersion);
                if (string.IsNullOrEmpty(javaPath))
                    throw new Exception("No se encontró Java instalado. Descarga e instala Java 17+ desde adoptium.net");
            }

            var progress = new Progress<double>(p => DownloadProgress = p);
            _launcherService.Log($"[INFO] Java seleccionado: {javaPath}");
            await _minecraftService.InstallLoaderAsync(
                mcVersion, loaderType, loaderVersion, javaPath,
                msg => { App.Current.Dispatcher.Invoke(() => LogStatus(msg)); },
                progress,
                onLog: _launcherService.Log);

            var loaderEnum = Enum.TryParse<ShoroCraftLauncher.Core.Enums.ProfileType>(loaderType, ignoreCase: true, out var parsed)
                ? parsed : SelectedProfile.Type;
            SelectedProfile.Type = loaderEnum;
            SelectedProfile.LoaderVersion = loaderVersion;
            SelectedProfile.MinecraftVersion = mcVersion;
            await _profileService.UpdateProfileAsync(SelectedProfile);
                
            ReadyStatus = "Listo";
            StatusMessage = $"{loaderType} {loaderVersion} instalado correctamente.";
            _launcherService.Log($"[INFO] {loaderType} {loaderVersion} instalado correctamente.");
        }
        catch (OperationCanceledException)
        {
            ReadyStatus = "Error";
            StatusMessage = $"La instalación de {loaderType} tardó demasiado y fue cancelada.";
            _launcherService.Log($"[ERROR] La instalación de {loaderType} tardó demasiado y fue cancelada.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install {Loader}", loaderType);
            ReadyStatus = "Error";
            StatusMessage = $"Error: {ex.Message}";
            _launcherService.Log($"[ERROR] Error instalando {loaderType}: {ex.Message}");
        }
        IsDownloading = false;
    }
}
