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

namespace ShoroCraftLauncher.App.ViewModels;

public class SettingsViewModel : BaseViewModel
{
    private readonly ISettingsRepository _settingsRepo;
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
        set => SetProperty(ref _gameDir, value);
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

    public void SetCurseForgeApiKeyFromUi(string key) => _curseForgeApiKey = key;

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
        ILogger<SettingsViewModel> logger,
        ILogService logService,
        IUpdaterService updaterService)
    {
        _settingsRepo = settingsRepo;
        _logger = logger;
        _logService = logService;
        _updaterService = updaterService;

        BrowseGameDirCommand = new RelayCommand(async _ => await BrowseGameDir());
        SaveGameDirCommand = new RelayCommand(async _ => await SaveGameDir());
        CleanTempCommand = new RelayCommand(async _ => await CleanTemp());
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
            CurseForgeApiKey = SecretProtector.Decrypt(settings.GetValueOrDefault("curseforge_api_key") ?? string.Empty);
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

    private async Task BrowseGameDir()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog();
        dialog.Description = "Selecciona la carpeta de Minecraft";
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            GameDir = dialog.SelectedPath;
    }

    private async Task SaveGameDir()
    {
        if (!string.IsNullOrEmpty(GameDir))
        {
            await _settingsRepo.SetAsync("game_directory", GameDir);
            await _settingsRepo.SetAsync("curseforge_api_key", SecretProtector.Encrypt(CurseForgeApiKey));
            _logService.Info("Settings", "GameDirectorySaved", "Directorio de Minecraft guardado.", new { GameDir });
            StatusMessage = "Configuracion guardada.";
        }
    }

    private async Task CleanTemp()
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
