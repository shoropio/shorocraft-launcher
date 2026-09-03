using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.App.Commands;
using ShoroCraftLauncher.App.Services;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.App.ViewModels;

public class MapsViewModel : BaseViewModel, IDisposable
{
    private readonly IGameMapService _mapService;
    private readonly ILogger<MapsViewModel> _logger;
    private readonly ILogService _logService;
    private readonly IDialogService _dialogService;

    public ObservableCollection<GameMap> Maps { get; } = new();
    public ObservableCollection<Profile> Profiles => _profileService.Profiles;

    private readonly IProfileService _profileService;

    public Profile? SelectedProfile
    {
        get => _profileService.SelectedProfile;
        set
        {
            _profileService.SelectedProfile = value;
            OnPropertyChanged(nameof(SelectedProfile));
            if (value != null) _ = LoadMapsAsync(value.Id);
        }
    }

    public ICommand AddMapCommand { get; }
    public ICommand RemoveMapCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand BackupWorldsCommand { get; }
    public ICommand BackupMapCommand { get; }

    public MapsViewModel(
        IGameMapService mapService,
        IProfileService profileService,
        IDialogService dialogService,
        ILogger<MapsViewModel> logger,
        ILogService logService)
    {
        _mapService = mapService;
        _profileService = profileService;
        _dialogService = dialogService;
        _logger = logger;
        _logService = logService;

        _profileService.SelectedProfileChanged += OnSelectedProfileChanged;
        AddMapCommand = new RelayCommand(async _ => await AddMap());
        RemoveMapCommand = new RelayCommand(async p => await RemoveMap(p));
        OpenFolderCommand = new RelayCommand(async _ => await OpenFolder());
        RefreshCommand = new RelayCommand(async _ => { if (SelectedProfile != null) await LoadMapsAsync(SelectedProfile.Id); });
        BackupWorldsCommand = new RelayCommand(async _ => await BackupWorlds());
        BackupMapCommand = new RelayCommand(async p => await BackupMap(p));

        SelectedProfile = _profileService.SelectedProfile ?? Profiles.FirstOrDefault();
    }

    private void OnSelectedProfileChanged()
    {
        SelectedProfile = _profileService.SelectedProfile;
    }

    public void Dispose()
    {
        _profileService.SelectedProfileChanged -= OnSelectedProfileChanged;
        GC.SuppressFinalize(this);
    }

    private async Task LoadMapsAsync(int profileId)
    {
        IsBusy = true;
        try
        {
            var profile = _profileService.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile != null)
                await _profileService.SyncProfileFilesAsync(profile);

            var maps = await _mapService.GetMapsAsync(profileId);
            Maps.Clear();
            foreach (var m in maps) Maps.Add(m);
            StatusMessage = $"{Maps.Count} mapas.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load maps");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddMap()
    {
        if (SelectedProfile == null) return;
        var files = _dialogService.ShowOpenFileDialog(
            "Mapas (*.zip;*.mcworld)|*.zip;*.mcworld",
            "Seleccionar mapas",
            multiselect: true);
        if (files == null) return;

        IsBusy = true;
        var added = 0;
        foreach (var file in files)
        {
            try
            {
                await _mapService.AddMapAsync(SelectedProfile.Id, file);
                added++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to add map {File}", file);
                StatusMessage = $"Error al agregar {Path.GetFileName(file)}: {ex.Message}";
            }
        }
        await LoadMapsAsync(SelectedProfile.Id);
        StatusMessage = $"{added} mapas agregados.";
        IsBusy = false;
    }

    private async Task RemoveMap(object? map)
    {
        if (map is not GameMap gameMap) return;

        var confirm = DialogHelper.Confirm(
            $"¿Eliminar el mapa '{gameMap.Name}'? Esta acción no se puede deshacer.",
            "Eliminar mapa");
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            await _mapService.RemoveMapAsync(gameMap);
            if (SelectedProfile != null) await LoadMapsAsync(SelectedProfile.Id);
            StatusMessage = "Mapa eliminado.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove map");
            StatusMessage = $"Error al eliminar el mapa: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task BackupWorlds()
    {
        if (SelectedProfile == null) return;
        IsBusy = true;
        try
        {
            await _profileService.CreateBackupAsync(SelectedProfile.Id, "Worlds");
            StatusMessage = "Respaldo de mundos creado correctamente.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to backup worlds");
            StatusMessage = $"Error al crear respaldo: {ex.Message}";
        }
        IsBusy = false;
    }

    private async Task BackupMap(object? map)
    {
        if (map is not GameMap gameMap) return;

        IsBusy = true;
        try
        {
            var zipPath = await _mapService.BackupMapAsync(gameMap);
            StatusMessage = $"Respaldo de '{gameMap.Name}' creado: {Path.GetFileName(zipPath)}";
            _logService.Info("Maps", "BackupCreated", StatusMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to backup map");
            StatusMessage = $"Error al respaldar el mundo: {ex.Message}";
            _logService.Error("Maps", "BackupFailed", $"Error al respaldar el mundo '{gameMap.Name}'.", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenFolder()
    {
        if (SelectedProfile == null) return;
        var folder = await _mapService.GetMapsFolderAsync(SelectedProfile.Id);
        if (Directory.Exists(folder))
            System.Diagnostics.Process.Start("explorer.exe", folder);
        else
            StatusMessage = "La carpeta de mapas no existe.";
    }
}
