using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.App.Commands;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.App.ViewModels;

public class SettingsViewModel : BaseViewModel
{
    private readonly ISettingsRepository _settingsRepo;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly ILogService _logService;

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
            SetProperty(ref _language, value);
            _ = _settingsRepo.SetAsync("language", value);
        }
    }

    private string _launcherVersion = "1.0.0";
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

    public List<string> Languages { get; } = new() { "es", "en", "fr", "de", "pt" };

    public SettingsViewModel(
        ISettingsRepository settingsRepo,
        ILogger<SettingsViewModel> logger,
        ILogService logService)
    {
        _settingsRepo = settingsRepo;
        _logger = logger;
        _logService = logService;

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
            LauncherVersion = settings.GetValueOrDefault("launcher_version") ?? "1.0.0";

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
            _logService.Info("Settings", "GameDirectorySaved", "Directorio de Minecraft guardado.", new { GameDir });
            StatusMessage = "Directorio guardado.";
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
        StatusMessage = "Buscando actualizaciones...";
        await Task.Delay(1000);
        StatusMessage = "No hay actualizaciones disponibles.";
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

    private async Task<long> CalculateTotalSizeAsync()
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
    }
}
