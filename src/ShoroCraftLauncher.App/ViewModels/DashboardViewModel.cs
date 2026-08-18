using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.App.Commands;
using ShoroCraftLauncher.App.Services;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.App.ViewModels;

public class StatCard : BaseViewModel
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public System.Windows.Media.Brush BarBrush { get; set; } = System.Windows.Media.Brushes.Transparent;
}

public class DashboardViewModel : BaseViewModel, IDisposable
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
    private readonly ILogger<DashboardViewModel> _logger;

    private const string LastNotifiedVersionKey = "last_notified_minecraft_version";

    public ObservableCollection<Profile> Profiles => _profileService.Profiles;
    public ObservableCollection<GameVersion> AvailableVersions { get; } = new();
    public ObservableCollection<StatCard> StatCards { get; } = new();

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
    public ICommand OptiFineInfoCommand { get; }
    public ICommand RepairProfileCommand { get; }
    public ICommand InstallMinecraftUpdateCommand { get; }
    public ICommand DismissMinecraftUpdateCommand { get; }
    public ICommand InstallFabricIrisSodiumCommand { get; }

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
        _logger = logger;

        _profileService.SelectedProfileChanged += OnSelectedProfileChanged;

        RefreshVersionsCommand = new RelayCommand(async _ => await LoadVersionsAsync());
        InstallVersionCommand = new RelayCommand(async p => await InstallVersion(p?.ToString() ?? "latest"), _ => !IsDownloading && !IsBusy);
        InstallLoaderCommand = new RelayCommand(async p => await InstallLoader(p?.ToString() ?? ""), _ => !IsDownloading && !IsBusy && SelectedProfile != null);
        ApplyProfileCommand = new RelayCommand(async _ => await ApplyProfile(), _ => !IsBusy);
        DownloadLauncherUpdateCommand = new RelayCommand(async _ => await InstallLauncherUpdateAsync(), _ => !IsBusy);
        
        InstallIrisCommand = new RelayCommand(async _ => await InstallIris(), _ => SelectedProfile != null && SelectedProfile.Type == ShoroCraftLauncher.Core.Enums.ProfileType.Fabric);
        OptiFineInfoCommand = new RelayCommand(_ => 
        {
            DialogHelper.Show("OptiFine no permite descargas automÃ¡ticas.\n\nSe abrirÃ¡ la pÃ¡gina oficial. Descarga la versiÃ³n correspondiente a tu juego, ve a la pestaÃ±a de 'Mods' en el Launcher y arrastra el archivo .jar descargado para instalarlo.", "OptiFine", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://optifine.net/downloads") { UseShellExecute = true });
        });
        RepairProfileCommand = new RelayCommand(async _ => await RepairProfile(), _ => SelectedProfile != null && !IsBusy && !IsDownloading);
        InstallMinecraftUpdateCommand = new RelayCommand(async _ => await InstallMinecraftUpdateAsync());
        DismissMinecraftUpdateCommand = new RelayCommand(async _ => await DismissMinecraftUpdateAsync());
        InstallFabricIrisSodiumCommand = new RelayCommand(async _ => await InstallFabricIrisSodium(), _ => !IsDownloading && !IsBusy && !IsIrisSodiumInstalled);

        InitializeStatCards();
    }

    private void InitializeStatCards()
    {
        StatCards.Clear();
        StatCards.Add(new StatCard
        {
            Label = TryGetString("Dash_Version") ?? "VersiÃ³n",
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
            var updateTask = _updaterService.CheckForUpdatesAsync(currentVersion);

            await Task.WhenAll(versionsTask, detailsTask, componentsTask, updateTask);

            var (isUpdateAvailable, latestVersion, downloadUrl, _) = updateTask.Result;
            if (isUpdateAvailable)
            {
                HasLauncherUpdate = true;
                _latestVersion = latestVersion;
                LauncherUpdateMessage = $"Â¡ShoroCraft Launcher {latestVersion} disponible!";
                _launcherUpdateUrl = downloadUrl;
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

    private async Task InstallLauncherUpdateAsync()
    {
        if (string.IsNullOrEmpty(_launcherUpdateUrl))
        {
            DialogHelper.Show("No se encontrÃ³ una actualizaciÃ³n disponible para descargar.",
                "Actualizar Launcher", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return;
        }

        IsInstallingUpdate = true;
        try
        {
            var installerPath = await _updaterService.DownloadUpdateAsync(_launcherUpdateUrl, _latestVersion ?? "latest");
            if (installerPath == null)
            {
                DialogHelper.Show("No se pudo descargar el instalador. Revisa tu conexiÃ³n e intÃ©ntalo de nuevo.",
                    "Error al actualizar", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            var result = DialogHelper.Show(
                "Se descargÃ³ la nueva versiÃ³n. El instalador se abrirÃ¡ y el Launcher se cerrarÃ¡. Â¿Continuar?",
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
            DialogHelper.Show("OcurriÃ³ un error al instalar la actualizaciÃ³n.", "Error",
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

            MarkInstalledVersions(stableVersions);

            AvailableVersions.Clear();
            foreach (var v in stableVersions)
                AvailableVersions.Add(v);

            if (stableVersions.Count > 0 && (string.IsNullOrWhiteSpace(SelectedVersion) || SelectedVersion == "latest"))
            {
                var installed = stableVersions.FirstOrDefault(v => v.IsInstalled);
                SelectedVersion = installed?.VersionId ?? stableVersions[0].VersionId;
            }

            if (stableVersions.Count > 0)
            {
                var latest = stableVersions[0].VersionId;
                _latestAvailableVersion = latest;
                if (SelectedProfile != null && SelectedProfile.Type == ShoroCraftLauncher.Core.Enums.ProfileType.Vanilla)
                {
                    if (SelectedProfile.MinecraftVersion != "latest" && SelectedProfile.MinecraftVersion != latest)
                    {
                        await CheckMinecraftUpdateNotificationAsync(latest);
                    }
                    else
                    {
                        HasUpdateNotification = false;
                    }
                }
                else
                {
                    HasUpdateNotification = false;
                }
            }

            if (stableVersions.Count == 0)
            {
                ReadyStatus = "Sin datos";
                StatusMessage = "No se pudieron obtener versiones estables.";
                _launcherService.Log($"[WARN] {StatusMessage}");
                return;
            }

            var installedStable = stableVersions.FirstOrDefault(v => v.IsInstalled);
            ReadyStatus = installedStable != null ? "Instalado" : "Listo";
            StatusMessage = installedStable != null
                ? $"VersiÃ³n estable instalada: {installedStable.VersionId}."
                : $"Lista actualizada. Ultima estable: {stableVersions[0].VersionId}.";
            _launcherService.Log($"[INFO] {StatusMessage}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch versions");
            ReadyStatus = "Error";
            StatusMessage = "Error al obtener versiones.";
            _launcherService.Log($"[ERROR] Error al obtener versiones: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CheckMinecraftUpdateNotificationAsync(string latest)
    {
        try
        {
            _latestAvailableVersion = latest;
            var lastNotified = await _settingsRepo.GetAsync(LastNotifiedVersionKey);
            if (!string.IsNullOrEmpty(lastNotified) && IsVersionAtLeast(lastNotified, latest))
            {
                HasUpdateNotification = false;
                return;
            }

            HasUpdateNotification = true;
            UpdateNotificationMessage = $"Â¡La nueva versiÃ³n de Minecraft {latest} ya estÃ¡ disponible!";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check Minecraft update notification");
            HasUpdateNotification = false;
        }
    }

    private static bool IsVersionAtLeast(string candidate, string baseline)
    {
        var candidateParts = candidate.Split('.').Select(part => int.TryParse(part, out var n) ? n : 0).ToArray();
        var baselineParts = baseline.Split('.').Select(part => int.TryParse(part, out var n) ? n : 0).ToArray();

        var length = Math.Max(candidateParts.Length, baselineParts.Length);
        for (int i = 0; i < length; i++)
        {
            var a = i < candidateParts.Length ? candidateParts[i] : 0;
            var b = i < baselineParts.Length ? baselineParts[i] : 0;
            if (a != b)
                return a > b;
        }
        return true;
    }

    private async Task InstallMinecraftUpdateAsync()
    {
        if (string.IsNullOrEmpty(_latestAvailableVersion))
        {
            HasUpdateNotification = false;
            return;
        }

        HasUpdateNotification = false;
        await InstallVersion(_latestAvailableVersion);
    }

    private async Task DismissMinecraftUpdateAsync()
    {
        HasUpdateNotification = false;
        if (!string.IsNullOrEmpty(_latestAvailableVersion))
        {
            try
            {
                await _settingsRepo.SetAsync(LastNotifiedVersionKey, _latestAvailableVersion);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist dismissed Minecraft version");
            }
        }
    }

    private async Task UpdateComponentInstallStatesAsync()
    {
        if (SelectedProfile == null)
        {
            IsIrisSodiumInstalled = false;
            return;
        }

        try
        {
            var knownNames = new List<string>();
            var mods = await _modService.GetModsAsync(SelectedProfile.Id);
            knownNames.AddRange(mods
                .Where(m => m.Status == ShoroCraftLauncher.Core.Enums.ModStatus.Active)
                .SelectMany(m => new[] { m.Name, m.FileName, m.ModVersion }));

            var modsDir = _minecraftService.GetModsDirectory(GetSelectedProfileGameDirectory());
            if (Directory.Exists(modsDir))
            {
                knownNames.AddRange(Directory.GetFiles(modsDir, "*.jar")
                    .Select(Path.GetFileNameWithoutExtension)
                    .Where(name => !string.IsNullOrWhiteSpace(name))!);
            }

            IsIrisSodiumInstalled = ContainsComponent(knownNames, "iris")
                && ContainsComponent(knownNames, "sodium");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect installed optimization mods");
            IsIrisSodiumInstalled = false;
        }
    }

    private static bool ContainsComponent(IEnumerable<string?> names, string component) =>
        names.Any(name => !string.IsNullOrWhiteSpace(name)
            && name.Contains(component, StringComparison.OrdinalIgnoreCase));

    private void MarkInstalledVersions(List<GameVersion> versions)
    {
        if (SelectedProfile == null)
            return;

        var gameDir = GetSelectedProfileGameDirectory();
        var versionsDir = Path.Combine(gameDir, "versions");
        if (!Directory.Exists(versionsDir))
            return;

        var installedNames = Directory.GetDirectories(versionsDir)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var version in versions)
        {
            version.IsInstalled = installedNames.Contains(version.VersionId)
                || installedNames.Any(installed =>
                    installed!.Contains(version.VersionId, StringComparison.OrdinalIgnoreCase)
                    && (installed.Contains("fabric", StringComparison.OrdinalIgnoreCase)
                        || installed.Contains("forge", StringComparison.OrdinalIgnoreCase)
                        || installed.Contains("neoforge", StringComparison.OrdinalIgnoreCase)
                        || installed.Contains("quilt", StringComparison.OrdinalIgnoreCase)
                        || installed.Contains("optifine", StringComparison.OrdinalIgnoreCase)));
        }
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

    private async Task InstallVersion(string? versionId)
    {
        if (string.IsNullOrEmpty(versionId)) return;
        if (IsDownloading || IsBusy) return;
        IsDownloading = true;
        DownloadProgress = 0;
        ReadyStatus = $"Instalando {versionId}...";
        StatusMessage = $"Instalando Minecraft {versionId}...";

        try
        {
            if (versionId.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                LogStatus("Resolviendo la versiÃ³n estable mÃ¡s nueva de Minecraft...");
                versionId = await _minecraftService.ResolveVersionIdAsync("latest");
                ReadyStatus = $"Instalando {versionId}...";
            }

            LogStatus($"Instalando Minecraft {versionId}...");
            var progress = new Progress<double>(p => DownloadProgress = p);
            await _minecraftService.InstallVersionAsync(versionId, progress, GetSelectedProfileGameDirectory());
            InstalledVersion = versionId;
            var installed = AvailableVersions.FirstOrDefault(v => string.Equals(v.VersionId, versionId, StringComparison.OrdinalIgnoreCase));
            if (installed != null)
                installed.IsInstalled = true;
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
        finally
        {
            IsDownloading = false;
        }
    }

    private async Task InstallLoader(string? loaderArg)
    {
        if (string.IsNullOrEmpty(loaderArg) || SelectedProfile == null) return;
        if (IsDownloading || IsBusy) return;
        var parts = loaderArg.Split(':');
        if (parts.Length < 2) return;

        var loaderType = parts[0];
        var loaderVersion = parts[1];

        IsDownloading = true;
        ReadyStatus = $"Preparando {loaderType}...";
        _launcherService.Log($"[INFO] Preparando instalaciÃ³n de {loaderType}...");
        StatusMessage = $"Preparando instalaciÃ³n de {loaderType}...";
        try
        {
            var mcVersion = SelectedProfile.MinecraftVersion;
            if (mcVersion.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                LogStatus("Resolviendo la versiÃ³n estable mÃ¡s nueva de Minecraft...");
                mcVersion = await _minecraftService.ResolveVersionIdAsync("latest");
            }

            ReadyStatus = $"Instalando {loaderType}...";

            if (loaderVersion.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = $"Obteniendo Ãºltima versiÃ³n de {loaderType}...";
                _launcherService.Log($"[INFO] Obteniendo la versiÃ³n estable mÃ¡s nueva de {loaderType} para Minecraft {mcVersion}...");
                var resolved = await _minecraftService.ResolveLatestLoaderVersionAsync(loaderType, mcVersion);
                if (resolved.Equals("latest", StringComparison.OrdinalIgnoreCase))
                    throw new Exception($"No se pudo determinar la Ãºltima versiÃ³n de {loaderType} para Minecraft {mcVersion}. Es posible que {loaderType} no tenga soporte para esa versiÃ³n.");
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
                    throw new Exception("No se encontrÃ³ Java instalado. Descarga e instala Java 17+ desde adoptium.net");
            }

            var progress = new Progress<double>(p => DownloadProgress = p);
            _launcherService.Log($"[INFO] Java seleccionado: {javaPath}");
            await _minecraftService.InstallLoaderAsync(
                mcVersion, loaderType, loaderVersion, javaPath,
                msg => { App.Current.Dispatcher.Invoke(() => LogStatus(msg)); },
                progress,
                onLog: _launcherService.Log,
                gameDir: GetSelectedProfileGameDirectory());

            var loaderEnum = Enum.TryParse<ShoroCraftLauncher.Core.Enums.ProfileType>(loaderType, ignoreCase: true, out var parsed)
                ? parsed : SelectedProfile.Type;
            SelectedProfile.Type = loaderEnum;
            SelectedProfile.LoaderVersion = loaderVersion;
            SelectedProfile.MinecraftVersion = mcVersion;
            await _profileService.UpdateProfileAsync(SelectedProfile);
                
            ReadyStatus = "Listo";
            StatusMessage = $"{loaderType} {loaderVersion} instalado correctamente.";
            _launcherService.Log($"[INFO] {loaderType} {loaderVersion} instalado correctamente.");
            await UpdateComponentInstallStatesAsync();
        }
        catch (OperationCanceledException)
        {
            ReadyStatus = "Error";
            StatusMessage = $"La instalaciÃ³n de {loaderType} tardÃ³ demasiado y fue cancelada.";
            _launcherService.Log($"[ERROR] La instalaciÃ³n de {loaderType} tardÃ³ demasiado y fue cancelada.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install {Loader}", loaderType);
            ReadyStatus = "Error";
            StatusMessage = $"Error: {ex.Message}";
            _launcherService.Log($"[ERROR] Error instalando {loaderType}: {ex.Message}");
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private async Task InstallIris()
    {
        if (SelectedProfile == null) return;
        if (IsIrisSodiumInstalled)
        {
            StatusMessage = "Iris + Sodium ya estÃ¡ instalado en este perfil.";
            ReadyStatus = "Instalado";
            return;
        }

        IsDownloading = true;
        ReadyStatus = "Instalando Iris & Sodium...";
        StatusMessage = "Descargando Iris (Shaders) y Sodium (Rendimiento)...";
        try
        {
            var irisMods = await _modService.SearchModsAsync("modrinth", "iris", SelectedProfile.MinecraftVersion, "fabric");
            if (irisMods.Any())
                await _modService.InstallFromSearchAsync(SelectedProfile.Id, irisMods.First(), "modrinth");

            var sodiumMods = await _modService.SearchModsAsync("modrinth", "sodium", SelectedProfile.MinecraftVersion, "fabric");
            if (sodiumMods.Any())
                await _modService.InstallFromSearchAsync(SelectedProfile.Id, sodiumMods.First(), "modrinth");
            
            StatusMessage = "Iris y Sodium instalados correctamente en tu perfil Fabric.";
            ReadyStatus = "Listo";
            await UpdateComponentInstallStatesAsync();
            _launcherService.Log("[INFO] Iris y Sodium instalados correctamente.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error instalando Iris: {ex.Message}";
            ReadyStatus = "Error";
            _logger.LogError(ex, "Error installing Iris");
            _launcherService.Log($"[ERROR] Error instalando Iris: {ex.Message}");
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private async Task InstallFabricIrisSodium()
    {
        IsDownloading = true;
        try
        {
            // 1. Ensure we have a Fabric profile
            Profile? fabricProfile = SelectedProfile;
            if (fabricProfile == null || fabricProfile.Type != ShoroCraftLauncher.Core.Enums.ProfileType.Fabric)
            {
                fabricProfile = Profiles.FirstOrDefault(p => p.Type == ShoroCraftLauncher.Core.Enums.ProfileType.Fabric);
                if (fabricProfile == null)
                {
                    ReadyStatus = "Creando perfil Fabric...";
                    StatusMessage = "No hay perfil Fabric. Creando uno nuevo...";
                    fabricProfile = new Profile
                    {
                        Name = "Fabric",
                        MinecraftVersion = "latest",
                        Type = ShoroCraftLauncher.Core.Enums.ProfileType.Fabric,
                        LoaderVersion = "latest",
                        MinRamMB = 1024,
                        MaxRamMB = 4096,
                        WindowWidth = 854,
                        WindowHeight = 480
                    };
                    await _profileRepo.CreateAsync(fabricProfile);
                    await _profileService.LoadProfilesAsync();
                    fabricProfile = Profiles.FirstOrDefault(p => p.Id == fabricProfile.Id) ?? fabricProfile;
                }
                SelectedProfile = fabricProfile;
            }

            var mcVersion = fabricProfile.MinecraftVersion;
            if (mcVersion.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                ReadyStatus = "Resolviendo versi\u00f3n de Minecraft...";
                StatusMessage = "Obteniendo \u00faltima versi\u00f3n estable...";
                mcVersion = await _minecraftService.ResolveVersionIdAsync("latest");
            }

            // 2. Install Minecraft version if needed
            ReadyStatus = "Verificando Minecraft...";
            StatusMessage = $"Verificando Minecraft {mcVersion}...";
            var versionsDir = Path.Combine(GetSelectedProfileGameDirectory(), "versions", mcVersion);
            if (!Directory.Exists(versionsDir) || !File.Exists(Path.Combine(versionsDir, $"{mcVersion}.jar")))
            {
                ReadyStatus = $"Instalando Minecraft {mcVersion}...";
                StatusMessage = $"Descargando Minecraft {mcVersion}...";
                var progress = new Progress<double>(p => DownloadProgress = p);
                await _minecraftService.InstallVersionAsync(mcVersion, progress, GetSelectedProfileGameDirectory());
            }

            // 3. Install Fabric loader if needed
            var loaderReady = await IsLoaderInstalledAsync(fabricProfile, mcVersion);
            if (!loaderReady)
            {
                ReadyStatus = "Instalando Fabric...";
                StatusMessage = "Descargando e instalando Fabric loader...";
                var loaderVersion = await _minecraftService.ResolveLatestLoaderVersionAsync("Fabric", mcVersion);
                if (loaderVersion.Equals("latest"))
                    throw new Exception("No se pudo determinar la \u00faltima versi\u00f3n de Fabric.");

                var javaPath = fabricProfile.JavaPath;
                if (string.IsNullOrEmpty(javaPath))
                {
                    javaPath = await _javaService.GetRecommendedJavaPathAsync(mcVersion);
                    if (string.IsNullOrEmpty(javaPath))
                        throw new Exception("No se encontr\u00f3 Java instalado.");
                }

                var loaderProgress = new Progress<double>(p => DownloadProgress = p);
                await _minecraftService.InstallLoaderAsync(
                    mcVersion, "Fabric", loaderVersion, javaPath,
                    msg => App.Current.Dispatcher.Invoke(() => LogStatus(msg)),
                    loaderProgress,
                    onLog: _launcherService.Log,
                    gameDir: GetSelectedProfileGameDirectory());

                fabricProfile.Type = ShoroCraftLauncher.Core.Enums.ProfileType.Fabric;
                fabricProfile.LoaderVersion = loaderVersion;
                fabricProfile.MinecraftVersion = mcVersion;
                await _profileService.UpdateProfileAsync(fabricProfile);
            }

            // 4. Install Iris + Sodium
            if (!IsIrisSodiumInstalled)
            {
                ReadyStatus = "Instalando Iris + Sodium...";
                StatusMessage = "Descargando Iris (Shaders) y Sodium (Rendimiento)...";

                var modrinthVersion = ToModrinthVersion(mcVersion);

                var irisMods = await _modService.SearchModsAsync("modrinth", "iris", modrinthVersion, "fabric");
                if (irisMods.Any())
                    await _modService.InstallFromSearchAsync(fabricProfile.Id, irisMods.First(), "modrinth");

                var sodiumMods = await _modService.SearchModsAsync("modrinth", "sodium", modrinthVersion, "fabric");
                if (sodiumMods.Any())
                    await _modService.InstallFromSearchAsync(fabricProfile.Id, sodiumMods.First(), "modrinth");
            }

            ReadyStatus = "Listo";
            StatusMessage = "Fabric + Iris + Sodium instalados correctamente.";
            _launcherService.Log("[INFO] Fabric + Iris + Sodium instalados correctamente.");
            await UpdateComponentInstallStatesAsync();
            await UpdateProfileDetailsAsync();
        }
        catch (OperationCanceledException)
        {
            ReadyStatus = "Error";
            StatusMessage = "La instalaci\u00f3n tard\u00f3 demasiado y fue cancelada.";
            _launcherService.Log("[ERROR] Instalaci\u00f3n de Fabric+Iris+Sodium cancelada por timeout.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install Fabric + Iris + Sodium");
            ReadyStatus = "Error";
            StatusMessage = $"Error: {ex.Message}";
            _launcherService.Log($"[ERROR] Error instalando Fabric+Iris+Sodium: {ex.Message}");
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private async Task<bool> IsLoaderInstalledAsync(Profile profile, string targetVersion)
    {
        var mcPath = new CmlLib.Core.MinecraftPath(GetSelectedProfileGameDirectory());
        var loaderPrefix = profile.Type.ToString().ToLower();
        var dirs = Directory.Exists(mcPath.Versions)
            ? Directory.GetDirectories(mcPath.Versions)
            : Array.Empty<string>();
        var match = dirs.Select(Path.GetFileName)
                        .FirstOrDefault(n => n != null
                            && n.Contains(loaderPrefix, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(
                                n.Split('-', StringSplitOptions.RemoveEmptyEntries).LastOrDefault(),
                                targetVersion,
                                StringComparison.OrdinalIgnoreCase));
        return match != null;
    }

    private async Task ValidateProfileChecklistAsync()
    {
        if (SelectedProfile == null)
        {
            IsJavaReady = false;
            IsVersionReady = false;
            IsLoaderReady = false;
            IsRamReady = false;
            ChecklistMessage = "Selecciona un perfil.";
            return;
        }

        try
        {
            var targetVersion = SelectedProfile.MinecraftVersion;
            if (string.Equals(targetVersion, "latest", StringComparison.OrdinalIgnoreCase))
            {
                targetVersion = await _minecraftService.ResolveVersionIdAsync("latest");
            }

            var gameDir = GetSelectedProfileGameDirectory();

            // 1. RAM check
            IsRamReady = SelectedProfile.MaxRamMB >= 1024 && SelectedProfile.MinRamMB <= SelectedProfile.MaxRamMB;

            // 2. Java check
            var javaPath = SelectedProfile.JavaPath;
            if (string.IsNullOrEmpty(javaPath))
            {
                javaPath = await _javaService.GetRecommendedJavaPathAsync(targetVersion);
            }
            IsJavaReady = !string.IsNullOrEmpty(javaPath) && System.IO.File.Exists(javaPath);

            // 3. Version check
            var mcPath = new CmlLib.Core.MinecraftPath(gameDir);
            var versionDir = System.IO.Path.Combine(mcPath.Versions, targetVersion);
            var jsonPath = System.IO.Path.Combine(versionDir, $"{targetVersion}.json");
            var jarPath = System.IO.Path.Combine(versionDir, $"{targetVersion}.jar");
            IsVersionReady = System.IO.File.Exists(jsonPath) && System.IO.File.Exists(jarPath);

            // 4. Loader check
            if (SelectedProfile.Type == ShoroCraftLauncher.Core.Enums.ProfileType.Vanilla)
            {
                IsLoaderReady = true;
            }
            else
            {
                var loaderPrefix = SelectedProfile.Type.ToString().ToLower();
                var dirs = System.IO.Directory.Exists(mcPath.Versions) 
                    ? System.IO.Directory.GetDirectories(mcPath.Versions) 
                    : Array.Empty<string>();
                var match = dirs.Select(System.IO.Path.GetFileName)
                                .FirstOrDefault(n => n != null
                                    && n.Contains(loaderPrefix, StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(
                                        n.Split('-', StringSplitOptions.RemoveEmptyEntries).LastOrDefault(),
                                        targetVersion,
                                        StringComparison.OrdinalIgnoreCase));
                IsLoaderReady = match != null;
            }

            if (IsJavaReady && IsVersionReady && IsLoaderReady && IsRamReady)
            {
                ChecklistMessage = "Perfil listo para jugar.";
            }
            else
            {
                var missing = new List<string>();
                if (!IsJavaReady) missing.Add("Java");
                if (!IsVersionReady) missing.Add("Minecraft");
                if (!IsLoaderReady) missing.Add(SelectedProfile.Type.ToString());
                if (!IsRamReady) missing.Add("AsignaciÃ³n de RAM");
                ChecklistMessage = "Falta: " + string.Join(", ", missing);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate profile checklist");
            ChecklistMessage = "Error al validar estado.";
        }
    }

    private async Task RepairProfile()
    {
        if (SelectedProfile == null) return;
        if (IsBusy || IsDownloading) return;
        IsBusy = true;
        ReadyStatus = "Reparando perfil...";
        LogStatus("Validando y reparando archivos clave...");

        try
        {
            var gameDir = GetSelectedProfileGameDirectory();

            _launcherService.Log("[INFO] Asegurando jerarquÃ­a de carpetas...");
            await _profileService.SyncProfileFilesAsync(SelectedProfile);
            await _minecraftService.RepairInstallationAsync(gameDir);

            var targetVersion = SelectedProfile.MinecraftVersion;
            if (targetVersion.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                targetVersion = await _minecraftService.ResolveVersionIdAsync("latest");
            }
            
            var mcPath = new CmlLib.Core.MinecraftPath(gameDir);
            var verPath = Path.Combine(mcPath.Versions, targetVersion);
            if (!Directory.Exists(verPath) || !File.Exists(Path.Combine(verPath, $"{targetVersion}.json")))
            {
                _launcherService.Log("[INFO] Descargando archivos de Minecraft faltantes...");
                var progress = new Progress<double>(p => DownloadProgress = p);
                await _minecraftService.InstallVersionAsync(targetVersion, progress, gameDir);
            }

            if (SelectedProfile.Type != ShoroCraftLauncher.Core.Enums.ProfileType.Vanilla)
            {
                await ValidateProfileChecklistAsync();
                if (!IsLoaderReady)
                {
                    var loaderType = SelectedProfile.Type.ToString();
                    var loaderVer = SelectedProfile.LoaderVersion;
                    if (string.IsNullOrEmpty(loaderVer) || loaderVer.Equals("latest", StringComparison.OrdinalIgnoreCase))
                    {
                        loaderVer = await _minecraftService.ResolveLatestLoaderVersionAsync(loaderType, targetVersion);
                    }

                    var javaPath = SelectedProfile.JavaPath;
                    if (string.IsNullOrEmpty(javaPath))
                    {
                        javaPath = await _javaService.GetRecommendedJavaPathAsync(targetVersion);
                    }

                    if (!string.IsNullOrEmpty(javaPath) && File.Exists(javaPath))
                    {
                        _launcherService.Log($"[INFO] Reinstalando cargador {loaderType} {loaderVer}...");
                        var progress = new Progress<double>(p => DownloadProgress = p);
                        await _minecraftService.InstallLoaderAsync(targetVersion, loaderType, loaderVer, javaPath, _ => {}, progress, _launcherService.Log, gameDir);
                    }
                }
            }

            LogStatus("ReparaciÃ³n finalizada.");
            ReadyStatus = "Listo";
            await UpdateProfileDetailsAsync();
            await UpdateComponentInstallStatesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Repair profile failed");
            ReadyStatus = "Error";
            StatusMessage = $"Error al reparar: {ex.Message}";
            _launcherService.Log($"[ERROR] Error al reparar perfil: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string ToModrinthVersion(string mcVersion)
    {
        if (string.IsNullOrWhiteSpace(mcVersion)) return mcVersion;
        var trimmed = mcVersion.Trim();
        if (trimmed.StartsWith("26.", StringComparison.OrdinalIgnoreCase))
        {
            var parts = trimmed.Split('.');
            if (parts.Length == 2 && int.TryParse(parts[1], out var minor))
            {
                return $"1.21.{minor}";
            }
        }
        return trimmed;
    }
}
