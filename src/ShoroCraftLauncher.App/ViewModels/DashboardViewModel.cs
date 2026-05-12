using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.App.Commands;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.App.ViewModels;

public class DashboardViewModel : BaseViewModel
{
    private readonly IProfileService _profileService;
    private readonly IGameVersionRepository _versionRepo;
    private readonly IMinecraftService _minecraftService;
    private readonly ILauncherService _launcherService;
    private readonly IJavaService _javaService;
    private readonly ILogger<DashboardViewModel> _logger;

    public ObservableCollection<Profile> Profiles => _profileService.Profiles;
    public ObservableCollection<GameVersion> AvailableVersions { get; } = new();
    public ObservableCollection<NewsItem> NewsFeed { get; } = new();

    public class NewsItem
    {
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
    }

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

        _profileService.SelectedProfileChanged += () =>
        {
            OnPropertyChanged(nameof(SelectedProfile));
            UpdateProfileDetailsAsync();
        };

        RefreshVersionsCommand = new RelayCommand(async _ => await LoadVersionsAsync());
        InstallVersionCommand = new RelayCommand(async p => await InstallVersion(p?.ToString() ?? "latest"));
        InstallLoaderCommand = new RelayCommand(async p => await InstallLoader(p?.ToString() ?? ""));
    }

    public async Task LoadDataAsync()
    {
        IsBusy = true;
        try
        {
            await _profileService.LoadProfilesAsync();
            await LoadVersionsAsync();
            await LoadNewsAsync();
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

    private async Task LoadNewsAsync()
    {
        try
        {
            // Placeholder news for now, or fetch from real RSS
            NewsFeed.Clear();
            NewsFeed.Add(new NewsItem 
            { 
                Title = "Actualización 1.21: Tricky Trials", 
                Summary = "Explora las nuevas Trial Chambers y el mazo.", 
                Date = "13 Jun 2024",
                ImageUrl = "https://www.minecraft.net/content/dam/minecraftnet/games/minecraft/key-art/TrickyTrials_KeyArt_16x9.png"
            });
            NewsFeed.Add(new NewsItem 
            { 
                Title = "Minecraft 15 Aniversario", 
                Summary = "Celebramos 15 años de bloques y aventuras.", 
                Date = "17 May 2024",
                ImageUrl = "https://www.minecraft.net/content/dam/minecraftnet/games/minecraft/key-art/15thAnniversary_KeyArt_16x9.png"
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load news feed");
        }
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

    private async Task LoadVersionsAsync()
    {
        IsBusy = true;
        ReadyStatus = "Obteniendo versiones...";
        StatusMessage = "Obteniendo versiones...";
        try
        {
            var versions = await _minecraftService.FetchAvailableVersionsAsync();
            AvailableVersions.Clear();
            foreach (var v in versions.Take(50))
                AvailableVersions.Add(v);
            ReadyStatus = "Listo";
            StatusMessage = $"{versions.Count} versiones disponibles.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch versions");
            ReadyStatus = "Error";
            StatusMessage = "Error al obtener versiones.";
        }
        IsBusy = false;
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
            var progress = new Progress<double>(p => DownloadProgress = p);
            await _minecraftService.InstallVersionAsync(versionId, progress);
            InstalledVersion = versionId;
            ReadyStatus = "Listo";
            StatusMessage = $"Minecraft {versionId} instalado.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Install failed");
            ReadyStatus = "Error";
            StatusMessage = $"Error: {ex.Message}";
        }
        IsDownloading = false;
    }

    private async Task InstallLoader(string? loaderArg)
    {
        if (string.IsNullOrEmpty(loaderArg) || SelectedProfile == null) return;
        var parts = loaderArg.Split(':');
        if (parts.Length < 2) return;

        IsDownloading = true;
        ReadyStatus = $"Instalando {parts[0]}...";
        StatusMessage = $"Instalando {parts[0]}...";
        try
        {
            var javaPath = SelectedProfile.JavaPath;
            if (string.IsNullOrEmpty(javaPath))
            {
                javaPath = await _javaService.GetRecommendedJavaPathAsync(SelectedProfile.MinecraftVersion);
                if (string.IsNullOrEmpty(javaPath))
                    throw new Exception("No se encontró Java instalado.");
            }

            var progress = new Progress<double>(p => DownloadProgress = p);
            await _minecraftService.InstallLoaderAsync(
                SelectedProfile.MinecraftVersion, parts[0], parts[1], javaPath,
                msg => { App.Current.Dispatcher.Invoke(() => StatusMessage = msg); },
                progress);
                
            ReadyStatus = "Listo";
            StatusMessage = $"{parts[0]} instalado.";
        }
        catch (Exception ex)
        {
            ReadyStatus = "Error";
            StatusMessage = $"Error: {ex.Message}";
        }
        IsDownloading = false;
    }
}
