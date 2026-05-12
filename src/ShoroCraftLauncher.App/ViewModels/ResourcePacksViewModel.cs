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
    private readonly IProfileRepository _profileRepo;
    private readonly ILogger<ResourcePacksViewModel> _logger;

    public ObservableCollection<ResourcePack> Packs { get; } = new();
    public ObservableCollection<Profile> Profiles { get; } = new();

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
        IProfileRepository profileRepo,
        ILogger<ResourcePacksViewModel> logger)
    {
        _packService = packService;
        _profileRepo = profileRepo;
        _logger = logger;

        AddPackCommand = new RelayCommand(async _ => await AddPack());
        TogglePackCommand = new RelayCommand(async p => await TogglePack(p));
        RemovePackCommand = new RelayCommand(async p => await RemovePack(p));
        OpenFolderCommand = new RelayCommand(async _ => await OpenFolder());
        RefreshCommand = new RelayCommand(async _ => { if (SelectedProfile != null) await LoadPacksAsync(SelectedProfile.Id); });

        _ = LoadProfilesAsync();
    }

    private async Task LoadProfilesAsync()
    {
        var profiles = await _profileRepo.GetAllAsync();
        Profiles.Clear();
        foreach (var p in profiles) Profiles.Add(p);
        SelectedProfile = profiles.FirstOrDefault();
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
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Resource Packs (*.zip)|*.zip",
            Multiselect = true,
            Title = "Seleccionar resource packs"
        };

        if (dialog.ShowDialog() == true)
        {
            IsBusy = true;
            var added = 0;
            foreach (var file in dialog.FileNames)
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
