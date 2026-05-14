using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.App.Commands;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.App.ViewModels;

public class ProfilesViewModel : BaseViewModel
{
    private readonly IProfileService _profileService;
    private readonly IProfileRepository _profileRepo;
    private readonly IMinecraftService _minecraftService;
    private readonly ILogger<ProfilesViewModel> _logger;
    private readonly ILogService _logService;

    public ObservableCollection<Profile> Profiles => _profileService.Profiles;

    public Profile? SelectedProfile
    {
        get => _profileService.SelectedProfile;
        set
        {
            _profileService.SelectedProfile = value;
            OnPropertyChanged(nameof(SelectedProfile));
            OnPropertyChanged(nameof(IsProfileSelected));
            if (value != null) LoadProfileIntoForm(value);
            else ClearForm();
        }
    }

    public bool IsProfileSelected => SelectedProfile != null;

    private string _profileName = string.Empty;
    public string ProfileName { get => _profileName; set => SetProperty(ref _profileName, value); }

    private string _mcVersion = "latest";
    public string McVersion { get => _mcVersion; set => SetProperty(ref _mcVersion, value); }

    private ProfileType _profileType = ProfileType.Vanilla;
    public ProfileType ProfileTypeValue { get => _profileType; set => SetProperty(ref _profileType, value); }

    private string _gameDir = string.Empty;
    public string GameDir { get => _gameDir; set => SetProperty(ref _gameDir, value); }

    private string _javaPath = string.Empty;
    public string JavaPath { get => _javaPath; set => SetProperty(ref _javaPath, value); }

    private int _minRam = 1024;
    public int MinRam { get => _minRam; set => SetProperty(ref _minRam, value); }

    private int _maxRam = 4096;
    public int MaxRam { get => _maxRam; set => SetProperty(ref _maxRam, value); }

    private int _windowWidth = 1280;
    public int WindowWidth { get => _windowWidth; set => SetProperty(ref _windowWidth, value); }

    private int _windowHeight = 720;
    public int WindowHeight { get => _windowHeight; set => SetProperty(ref _windowHeight, value); }

    private string _jvmArgs = string.Empty;
    public string JvmArgs { get => _jvmArgs; set => SetProperty(ref _jvmArgs, value); }

    private string _loaderVersion = string.Empty;
    public string LoaderVersion { get => _loaderVersion; set => SetProperty(ref _loaderVersion, value); }

    private bool _isFullscreen;
    public bool IsFullscreen { get => _isFullscreen; set => SetProperty(ref _isFullscreen, value); }

    public List<ProfileType> ProfileTypes { get; } = Enum.GetValues<ProfileType>().ToList();

    public ICommand CreateProfileCommand { get; }
    public ICommand SaveProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    public ICommand DuplicateProfileCommand { get; }
    public ICommand BrowseGameDirCommand { get; }
    public ICommand BrowseJavaCommand { get; }
    public ICommand OpenFolderCommand { get; }

    public ProfilesViewModel(
        IProfileService profileService,
        IProfileRepository profileRepo,
        IMinecraftService minecraftService,
        ILogger<ProfilesViewModel> logger,
        ILogService logService)
    {
        _profileService = profileService;
        _profileRepo = profileRepo;
        _minecraftService = minecraftService;
        _logger = logger;
        _logService = logService;

        _profileService.SelectedProfileChanged += () =>
        {
            OnPropertyChanged(nameof(SelectedProfile));
            OnPropertyChanged(nameof(IsProfileSelected));
            if (SelectedProfile != null) LoadProfileIntoForm(SelectedProfile);
            else ClearForm();
        };

        CreateProfileCommand = new RelayCommand(async _ => await CreateProfile());
        SaveProfileCommand = new RelayCommand(async _ => await SaveProfile(), _ => SelectedProfile != null);
        DeleteProfileCommand = new RelayCommand(async _ => await DeleteProfile(), _ => SelectedProfile != null);
        DuplicateProfileCommand = new RelayCommand(async _ => await DuplicateProfile(), _ => SelectedProfile != null);
        BrowseGameDirCommand = new RelayCommand(async _ => await BrowseGameDirectory());
        BrowseJavaCommand = new RelayCommand(async _ => await BrowseJavaPath());
        OpenFolderCommand = new RelayCommand(async _ => await OpenFolder(), _ => SelectedProfile != null);

        _ = LoadProfilesAsync();
    }

    public async Task LoadProfilesAsync()
    {
        IsBusy = true;
        try
        {
            await _profileService.LoadProfilesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load profiles");
            StatusMessage = "Error al cargar perfiles.";
        }
        IsBusy = false;
    }

    private void LoadProfileIntoForm(Profile profile)
    {
        ProfileName = profile.Name;
        McVersion = profile.MinecraftVersion;
        ProfileTypeValue = profile.Type;
        GameDir = profile.GameDirectory;
        JavaPath = profile.JavaPath;
        MinRam = profile.MinRamMB;
        MaxRam = profile.MaxRamMB;
        WindowWidth = profile.WindowWidth;
        WindowHeight = profile.WindowHeight;
        JvmArgs = profile.JvmArguments;
        LoaderVersion = profile.LoaderVersion;
        IsFullscreen = profile.IsFullscreen;
    }

    private async Task CreateProfile()
    {
        IsBusy = true;
        try
        {
            var profile = new Profile
            {
                Name = "Nuevo Perfil",
                MinecraftVersion = "latest",
                Type = ProfileType.Vanilla,
                MinRamMB = 1024,
                MaxRamMB = 4096,
                WindowWidth = 854,
                WindowHeight = 480
            };

            await _profileRepo.CreateAsync(profile);
            await LoadProfilesAsync();
            
            SelectedProfile = Profiles.FirstOrDefault(p => p.Name == "Nuevo Perfil") ?? Profiles.LastOrDefault();
            StatusMessage = "Nuevo perfil creado. Edita los detalles y haz clic en Guardar.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create profile");
            StatusMessage = $"Error: {ex.Message}";
        }
        IsBusy = false;
    }

    private async Task SaveProfile()
    {
        if (SelectedProfile == null) return;

        IsBusy = true;
        try
        {
            var validationError = ValidateProfileForm();
            if (validationError != null)
            {
                _logService.Warning("Profile", "ValidationFailed", validationError, new { ProfileName, McVersion, ProfileTypeValue, MinRam, MaxRam, WindowWidth, WindowHeight });
                StatusMessage = validationError;
                IsBusy = false;
                return;
            }

            SelectedProfile.Name = ProfileName;
            SelectedProfile.MinecraftVersion = McVersion;
            SelectedProfile.Type = ProfileTypeValue;
            SelectedProfile.GameDirectory = GameDir;
            SelectedProfile.JavaPath = JavaPath;
            SelectedProfile.MinRamMB = MinRam;
            SelectedProfile.MaxRamMB = MaxRam;
            SelectedProfile.WindowWidth = WindowWidth;
            SelectedProfile.WindowHeight = WindowHeight;
            SelectedProfile.JvmArguments = JvmArgs;
            SelectedProfile.LoaderVersion = LoaderVersion;
            SelectedProfile.IsFullscreen = IsFullscreen;

            await _profileRepo.UpdateAsync(SelectedProfile);
            _logService.Info("Profile", "Saved", "Perfil actualizado.", new { SelectedProfile.Id, SelectedProfile.Name, SelectedProfile.MinecraftVersion, SelectedProfile.Type });
            await LoadProfilesAsync();
            StatusMessage = "Perfil actualizado.";
        }
        catch (Exception ex)
        {
            _logService.Error("Profile", "SaveFailed", "Error al guardar perfil.", ex);
            StatusMessage = $"Error: {ex.Message}";
        }
        IsBusy = false;
    }

    private async Task DeleteProfile()
    {
        if (SelectedProfile == null) return;

        IsBusy = true;
        try
        {
            await _profileRepo.DeleteAsync(SelectedProfile.Id);
            await LoadProfilesAsync();
            ClearForm();
            StatusMessage = "Perfil eliminado.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        IsBusy = false;
    }

    private async Task DuplicateProfile()
    {
        if (SelectedProfile == null) return;

        IsBusy = true;
        try
        {
            var duplicate = new Profile
            {
                Name = SelectedProfile.Name + " (copia)",
                MinecraftVersion = SelectedProfile.MinecraftVersion,
                Type = SelectedProfile.Type,
                GameDirectory = SelectedProfile.GameDirectory + "_copy",
                JavaPath = SelectedProfile.JavaPath,
                MinRamMB = SelectedProfile.MinRamMB,
                MaxRamMB = SelectedProfile.MaxRamMB,
                WindowWidth = SelectedProfile.WindowWidth,
                WindowHeight = SelectedProfile.WindowHeight,
                JvmArguments = SelectedProfile.JvmArguments,
                LoaderVersion = SelectedProfile.LoaderVersion,
                IsFullscreen = SelectedProfile.IsFullscreen
            };

            await _profileRepo.CreateAsync(duplicate);
            await LoadProfilesAsync();
            StatusMessage = "Perfil duplicado.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        IsBusy = false;
    }

    private void ClearForm()
    {
        ProfileName = string.Empty;
        McVersion = "latest";
        ProfileTypeValue = ProfileType.Vanilla;
        GameDir = string.Empty;
        JavaPath = string.Empty;
        MinRam = 1024;
        MaxRam = 4096;
        WindowWidth = 854;
        WindowHeight = 480;
        JvmArgs = string.Empty;
        LoaderVersion = string.Empty;
        IsFullscreen = false;
    }

    private string? ValidateProfileForm()
    {
        if (string.IsNullOrWhiteSpace(ProfileName))
            return "El perfil necesita un nombre.";

        if (string.IsNullOrWhiteSpace(McVersion))
            return "Selecciona una versión de Minecraft.";

        if (MinRam < 512)
            return "La RAM mínima debe ser al menos 512 MB.";

        if (MaxRam < MinRam)
            return "La RAM máxima no puede ser menor que la mínima.";

        if (WindowWidth < 320 || WindowHeight < 240)
            return "La ventana configurada es demasiado pequeña.";

        if (!string.IsNullOrWhiteSpace(JavaPath) && !File.Exists(JavaPath))
            return "La ruta de Java seleccionada no existe.";

        if (!string.IsNullOrWhiteSpace(GameDir))
        {
            try
            {
                Directory.CreateDirectory(GameDir);
                var probe = Path.Combine(GameDir, ".shorocraft-write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
            }
            catch
            {
                return "El directorio del perfil no se puede escribir.";
            }
        }

        if (ProfileTypeValue != ProfileType.Vanilla && string.IsNullOrWhiteSpace(LoaderVersion))
            return "Este perfil usa loader; instala o define una versión de loader.";

        return null;
    }

    private async Task BrowseGameDirectory()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog();
        dialog.Description = "Selecciona la carpeta de Minecraft";
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            GameDir = dialog.SelectedPath;
    }

    private async Task BrowseJavaPath()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Java executable (javaw.exe)|javaw.exe|Java executable (java.exe)|java.exe",
            Title = "Selecciona Java"
        };
        if (dialog.ShowDialog() == true)
            JavaPath = dialog.FileName;
    }

    private async Task OpenFolder()
    {
        if (SelectedProfile == null) return;
        var dir = string.IsNullOrEmpty(SelectedProfile.GameDirectory)
            ? _minecraftService.GetDefaultGameDirectory(SelectedProfile.Name)
            : SelectedProfile.GameDirectory;

        if (Directory.Exists(dir))
            System.Diagnostics.Process.Start("explorer.exe", dir);
        else
            StatusMessage = "La carpeta del perfil no existe.";
    }
}
