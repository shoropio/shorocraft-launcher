using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.App.Commands;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.App.ViewModels;

public class ResourcePacksViewModel : BaseViewModel
{
    private readonly IResourcePackService _packService;
    private readonly ILogger<ResourcePacksViewModel> _logger;
    private readonly IDialogService _dialogService;

    public ObservableCollection<ResourcePack> Packs { get; } = new();
    public ObservableCollection<Profile> Profiles => _profileService.Profiles;

    private readonly IProfileService _profileService;

    private Profile? _selectedProfile;
    public Profile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            SetProperty(ref _selectedProfile, value);
            if (value != null) _ = LoadPacksAsync(value.Id);
        }
    }

    public ICommand AddPackCommand { get; }
    public ICommand TogglePackCommand { get; }
    public ICommand RemovePackCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand RefreshCommand { get; }

    public ResourcePacksViewModel(
        IResourcePackService packService,
        IProfileService profileService,
        IDialogService dialogService,
        ILogger<ResourcePacksViewModel> logger)
    {
        _packService = packService;
        _profileService = profileService;
        _dialogService = dialogService;
        _logger = logger;

        AddPackCommand = new RelayCommand(async _ => await AddPack());
        TogglePackCommand = new RelayCommand(async p => await TogglePack(p));
        RemovePackCommand = new RelayCommand(async p => await RemovePack(p));
        OpenFolderCommand = new RelayCommand(async _ => await OpenFolder());
        RefreshCommand = new RelayCommand(async _ => { if (SelectedProfile != null) await LoadPacksAsync(SelectedProfile.Id); });

        SelectedProfile = _profileService.SelectedProfile ?? Profiles.FirstOrDefault();
    }

    private async Task LoadPacksAsync(int profileId)
    {
        IsBusy = true;
        try
        {
            var packs = await _packService.GetPacksAsync(profileId);
            Packs.Clear();
            foreach (var p in packs) Packs.Add(p);
            StatusMessage = $"{Packs.Count} resource packs.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load resource packs");
            StatusMessage = $"Error: {ex.Message}";
        }
        IsBusy = false;
    }

    private async Task AddPack()
    {
        if (SelectedProfile == null) return;
        var files = _dialogService.ShowOpenFileDialog(
            "Resource Packs (*.zip)|*.zip",
            "Seleccionar resource packs",
            multiselect: true);
        if (files == null) return;

        IsBusy = true;
        var added = 0;
        foreach (var file in files)
        {
            try
            {
                await _packService.AddPackAsync(SelectedProfile.Id, file);
                added++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to add pack {File}", file);
            }
        }
        await LoadPacksAsync(SelectedProfile.Id);
        StatusMessage = $"{added} packs agregados.";
        IsBusy = false;
    }

    private async Task TogglePack(object? packId)
    {
        if (packId is int id)
        {
            await _packService.TogglePackAsync(id);
            if (SelectedProfile != null) await LoadPacksAsync(SelectedProfile.Id);
        }
    }

    private async Task RemovePack(object? packId)
    {
        if (packId is int id)
        {
            await _packService.RemovePackAsync(id);
            if (SelectedProfile != null) await LoadPacksAsync(SelectedProfile.Id);
        }
    }

    private async Task OpenFolder()
    {
        if (SelectedProfile == null) return;
        var folder = await _packService.GetPacksFolderAsync(SelectedProfile.Id);
        if (Directory.Exists(folder))
            System.Diagnostics.Process.Start("explorer.exe", folder);
    }
}
