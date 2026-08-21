using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.App.Commands;
using ShoroCraftLauncher.App.Services;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Infrastructure;

namespace ShoroCraftLauncher.App.ViewModels;

public class SettingsViewModel : BaseViewModel
{
    private readonly ISettingsRepository _settingsRepo;
    private readonly ISecretStorage _secretStorage;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly ILogService _logService;
    private readonly IUpdaterService _updaterService;

    private bool _closeOnLaunch;
    public bool CloseOnLaunch
    {
        get => _closeOnLaunch;
        set
        {
            SetProperty(ref _closeOnLaunch, value);
            _ = _settingsRepo.SetAsync("close_launcher_on_launch", value.ToString());
        }
    }

    private bool _keepOpen;
    public bool KeepOpen
    {
        get => _keepOpen;
        set
        {
            SetProperty(ref _keepOpen, value);
            _ = _settingsRepo.SetAsync("keep_launcher_open", value.ToString());
        }
    }

    private string _gameDir = string.Empty;
    public string GameDir
    {
        get => _gameDir;
        set
        {
            if (SetProperty(ref _gameDir, value))
                GameDirValidationVisible = false;
        }
    }

    private bool _gameDirValidationVisible;
    public bool GameDirValidationVisible
    {
        get => _gameDirValidationVisible;
        set => SetProperty(ref _gameDirValidationVisible, value);
    }

    private string _gameDirValidationText = string.Empty;
    public string GameDirValidationText
    {
        get => _gameDirValidationText;
        set => SetProperty(ref _gameDirValidationText, value);
    }

    private string _language = "es";
    public string Language
    {
        get => _language;
        set
        {
            if (SetProperty(ref _language, value))
            {
                _ = _settingsRepo.SetAsync("language", value);
                ApplyLanguage(value);
            }
        }
    }

    private static void ApplyLanguage(string language)
    {
        var dicts = System.Windows.Application.Current.Resources.MergedDictionaries;
        var existing = dicts.FirstOrDefault(d => d.Source != null && d.Source.OriginalString.StartsWith("Locales/"));
        if (existing != null)
            dicts.Remove(existing);
        var locale = language == "es" ? "es-ES" : "en-US";
        dicts.Add(new ResourceDictionary { Source = new Uri($"Locales/{locale}.xaml", UriKind.Relative) });
    }

    private string _curseForgeApiKey = string.Empty;
    public event EventHandler<string>? CurseForgeApiKeyChanged;
    public string CurseForgeApiKey
    {
        get => _curseForgeApiKey;
        set
        {
            if (SetProperty(ref _curseForgeApiKey, value))
                CurseForgeApiKeyChanged?.Invoke(this, value);
        }
    }

    public void SetCurseForgeApiKeyFromUi(string key)
    {
        if (SetProperty(ref _curseForgeApiKey, key))
        {
            CurseForgeApiKeyChanged?.Invoke(this, key);
            // Store API key securely in Windows Credential Locker
            _ = _secretStorage.SetSecretAsync("curseforge_api_key", key);
        }
    }

    private string _launcherVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";
    public string LauncherVersion
    {
        get => _launcherVersion;
        set => SetProperty(ref _launcherVersion, value);
    }

    private long _totalSizeBytes;
    public string TotalSizeFormatted
    {
        get
        {
            return _totalSizeBytes switch
            {
                >= 1073741824 => $"{_totalSizeBytes / 1073741824.0:F1} GB",
                >= 1048576 => $"{_totalSizeBytes / 1048576.0:F0} MB",
                >= 1024 => $"{_totalSizeBytes / 1024.0:F0} KB",
                _ => $"{_totalSizeBytes} B"
            };
        }
    }

    public ICommand BrowseGameDirCommand { get; }
    public ICommand SaveGameDirCommand { get; }
    public ICommand CleanTempCommand { get; }
    public ICommand CheckUpdatesCommand { get; }
    public ICommand OpenLogsFolderCommand { get; }
    public ICommand ExportDiagnosticsCommand { get; }

    public List<string> Languages { get; } = new() { "es", "en" };

    public SettingsViewModel(
        ISettingsRepository settingsRepo,
        ISecretStorage secretStorage,
        ILogger<SettingsViewModel> logger,
        ILogService logService,
        IUpdaterService updaterService)
    {
        _settingsRepo = settingsRepo;
        _secretStorage = secretStorage;
        _logger = logger;
        _logService = logService;
        _updaterService = updaterService;

        BrowseGameDirCommand = new RelayCommand(_ => BrowseGameDir());
        SaveGameDirCommand = new RelayCommand(async _ => await SaveGameDir());
        CleanTempCommand = new RelayCommand(_ => CleanTemp());
        CheckUpdatesCommand = new RelayCommand(async _ => await CheckUpdates());
        OpenLogsFolderCommand = new RelayCommand(_ => OpenLogsFolder());
        ExportDiagnosticsCommand = new RelayCommand(async _ => await ExportDiagnostics());

        _ = LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        IsBusy = true;
        try
        {
            var settings = await _settingsRepo.GetAllAsync();

            CloseOnLaunch = settings.GetValueOrDefault("close_launcher_on_launch") == "true";
            KeepOpen = settings.GetValueOrDefault("keep_launcher_open") != "false";
            GameDir = settings.GetValueOrDefault("game_directory") ?? string.Empty;
            Language = settings.GetValueOrDefault("language") ?? "es";

            // Get CurseForge API key from secure storage (with automatic migration from DPAPI)
            var apiKey = await GetCurseForgeApiKeyAsync();
            CurseForgeApiKey = apiKey;

            LauncherVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";

            _totalSizeBytes = await CalculateTotalSizeAsync();
            OnPropertyChanged(nameof(TotalSizeFormatted));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings");
            _logService.Error("Settings", "LoadFailed", "Error al cargar configuración.", ex);
            StatusMessage = "Error al cargar configuración.";
        }
        IsBusy = false;
    }

    private async Task<string?> GetCurseForgeApiKeyAsync()
    {
        // Try to get from secure storage first
        var secret = await _secretStorage.GetSecretAsync("curseforge_api_key");
        if (secret != null)
        {
            return secret;
        }

        // Fallback: try database with DPAPI decryption (migration from old format)
        var dbKey = await _settingsRepo.GetAsync("curseforge_api_key");
        if (!string.IsNullOrWhiteSpace(dbKey))
        {
            try
            {
                var decrypted = SecretProtector.Decrypt(dbKey);
                // Migrate to secure storage
                await _secretStorage.SetSecretAsync("curseforge_api_key", decrypted);
                // Remove from database after successful migration
                await _settingsRepo.RemoveFromDatabaseAsync("curseforge_api_key");
                _logger.LogInformation("CurseForge API key migrated from DPAPI to secure storage");
                return decrypted;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to decrypt and migrate CurseForge API key from DPAPI");
            }
        }

        return null;
    }

    private void BrowseGameDir()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Selecciona la carpeta de Minecraft"
        };
        if (dialog.ShowDialog() == true)
            GameDir = dialog.FolderName;
    }

    private async Task SaveGameDir()
    {
        if (string.IsNullOrWhiteSpace(GameDir))
        {
            GameDirValidationText = "Selecciona una carpeta válida.";
            GameDirValidationVisible = true;
            return;
        }

        if (!Directory.Exists(GameDir))
        {
            GameDirValidationText = "La carpeta seleccionada no existe.";
            GameDirValidationVisible = true;
            return;
        }

        // Verify it's a valid Minecraft directory (has versions folder or can create one)
        var versionsPath = Path.Combine(GameDir, "versions");
        if (!Directory.Exists(versionsPath))
        {
            try { Directory.CreateDirectory(versionsPath); }
            catch
            {
                GameDirValidationText = "No se puede crear la carpeta versions en la ruta seleccionada.";
                GameDirValidationVisible = true;
                return;
            }
        }

        GameDirValidationVisible = false;
        await _settingsRepo.SetAsync("game_directory", GameDir);
        // API key now stored securely via ISecretStorage, not in SettingsRepository
        // The CurseForgeApiKey property is updated separately if changed via UI
        _logService.Info("Settings", "GameDirectorySaved", "Directorio de Minecraft guardado.", new { GameDir });
        StatusMessage = "Configuración guardada.";
    }

    private void CleanTemp()
    {
        IsBusy = true;
        try
        {
            var tempDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ShoroCraftLauncher", "temp");

            if (Directory.Exists(tempDir))
            {
                foreach (var file in Directory.GetFiles(tempDir))
                {
                    try { File.Delete(file); } catch { }
                }
                StatusMessage = "Archivos temporales eliminados.";
            }
            else
            {
                StatusMessage = "No hay archivos temporales.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        IsBusy = false;
    }

    private async Task CheckUpdates()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "Buscando actualizaciones...";
        try
        {
            var currentVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0";
            var (isUpdateAvailable, latestVersion, downloadUrl, _) = await _updaterService.CheckForUpdatesAsync(currentVersion);

            if (isUpdateAvailable && !string.IsNullOrEmpty(downloadUrl))
            {
                var result = DialogHelper.Show(
                    $"Hay una nueva versión disponible: {latestVersion}\n\n¿Quieres descargarla e instalarla?",
                    "Actualización disponible",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Information);
                if (result == System.Windows.MessageBoxResult.Yes)
                {
                    await InstallUpdateAsync(downloadUrl, latestVersion ?? string.Empty);
                    return;
                }
            }

            StatusMessage = isUpdateAvailable
                ? $"Actualización disponible: {latestVersion}. Puedes instalarla desde el dashboard."
                : "No hay actualizaciones disponibles.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check for updates");
            _logService.Error("Settings", "CheckUpdatesFailed", "Error al buscar actualizaciones.", ex);
            StatusMessage = "Error al buscar actualizaciones.";
        }
        IsBusy = false;
    }

    private async Task InstallUpdateAsync(string downloadUrl, string version)
    {
        StatusMessage = "Descargando actualización...";
        try
        {
            var installerPath = await _updaterService.DownloadUpdateAsync(downloadUrl, version);
            if (installerPath == null)
            {
                StatusMessage = "No se pudo descargar el instalador. Revisa tu conexión.";
                return;
            }

            var confirm = DialogHelper.Show(
                "Actualización descargada. El instalador se abrirá y el Launcher se cerrará. ¿Continuar?",
                "Actualizar Launcher",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);
            if (confirm != System.Windows.MessageBoxResult.Yes)
                return;

            await _updaterService.LaunchInstallerAsync(installerPath);
            System.Windows.Application.Current?.Shutdown();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update install failed");
            StatusMessage = $"Error al instalar la actualización: {ex.Message}";
        }
    }

    private void OpenLogsFolder()
    {
        try
        {
            Directory.CreateDirectory(_logService.SessionDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = _logService.SessionDirectory,
                UseShellExecute = true
            });
            StatusMessage = "Carpeta de logs abierta.";
            _logService.Info("Diagnostics", "LogsFolderOpened", "Carpeta de logs abierta.", new { _logService.SessionDirectory });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open logs folder");
            _logService.Error("Diagnostics", "OpenLogsFolderFailed", "No se pudo abrir la carpeta de logs.", ex);
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    private async Task ExportDiagnostics()
    {
        IsBusy = true;
        try
        {
            StatusMessage = "Exportando diagnóstico...";
            var zipPath = await _logService.ExportDiagnosticsZipAsync(new DiagnosticExportOptions());
            StatusMessage = $"Diagnóstico exportado: {zipPath}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export diagnostics");
            _logService.Error("Diagnostics", "ExportFailed", "No se pudo exportar diagnóstico.", ex);
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task<long> CalculateTotalSizeAsync() => Task.Run(() =>
    {
        long total = 0;
        var baseDir = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShoroCraftLauncher")
        };

        foreach (var dir in baseDir)
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(file).Length; } catch { }
                }
            }
            catch { }
        }

        return total;
    });
}