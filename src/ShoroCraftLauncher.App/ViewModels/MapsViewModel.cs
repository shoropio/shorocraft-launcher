using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.App.Commands;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.App.ViewModels;

public class MapsViewModel : BaseViewModel
{
    private readonly IGameMapService _mapService;
    private readonly ILogger<MapsViewModel> _logger;
    private readonly ILogService _logService;
    private readonly IDialogService _dialogService;

    public ObservableCollection<GameMap> Maps { get; } = new();
    public ObservableCollection<Profile> Profiles => _profileService.Profiles;

    private readonly IProfileService _profileService;

    private Profile? _selectedProfile;
    public Profile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            SetProperty(ref _selectedProfile, value);
            if (value != null) _ = LoadMapsAsync(value.Id);
        }
    }

    public ICommand AddMapCommand { get; }
    public ICommand RemoveMapCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand RefreshCommand { get; }

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

        AddMapCommand = new RelayCommand(async _ => await AddMap());
        RemoveMapCommand = new RelayCommand(async p => await RemoveMap(p));
        OpenFolderCommand = new RelayCommand(async _ => await OpenFolder());
        RefreshCommand = new RelayCommand(async _ => { if (SelectedProfile != null) await LoadMapsAsync(SelectedProfile.Id); });

        SelectedProfile = _profileService.SelectedProfile ?? Profiles.FirstOrDefault();
    }

    private async Task LoadMapsAsync(int profileId)
    {
        IsBusy = true;
        try
        {
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
        IsBusy = false;
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

    private async Task RemoveMap(object? mapId)
    {
        if (mapId is int id)
        {
            await _mapService.RemoveMapAsync(id);
            if (SelectedProfile != null) await LoadMapsAsync(SelectedProfile.Id);
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
