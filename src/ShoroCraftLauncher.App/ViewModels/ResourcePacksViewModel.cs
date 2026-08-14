using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.App.Commands;
using ShoroCraftLauncher.App.Services;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.App.ViewModels;

public class ResourcePacksViewModel : BaseViewModel, IDisposable
{
    private readonly IResourcePackService _packService;
    private readonly ILogger<ResourcePacksViewModel> _logger;
    private readonly IDialogService _dialogService;

    public ObservableCollection<ResourcePack> Packs { get; } = new();
    public ObservableCollection<Profile> Profiles => _profileService.Profiles;

    private readonly IProfileService _profileService;

    public Profile? SelectedProfile
    {
        get => _profileService.SelectedProfile;
        set
        {
            _profileService.SelectedProfile = value;
            OnPropertyChanged(nameof(SelectedProfile));
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

        _profileService.SelectedProfileChanged += OnSelectedProfileChanged;
        AddPackCommand = new RelayCommand(async _ => await AddPack());
        TogglePackCommand = new RelayCommand(async p => await TogglePack(p));
        RemovePackCommand = new RelayCommand(async p => await RemovePack(p));
        OpenFolderCommand = new RelayCommand(async _ => await OpenFolder());
        RefreshCommand = new RelayCommand(async _ => { if (SelectedProfile != null) await LoadPacksAsync(SelectedProfile.Id); });

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

    private async Task LoadPacksAsync(int profileId)
    {
        IsBusy = true;
        try
        {
            var profile = _profileService.Profiles.FirstOrDefault(p => p.Id == profileId);
            if (profile != null)
                await _profileService.SyncProfileFilesAsync(profile);

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
        finally
        {
            IsBusy = false;
        }
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
        if (packId is not int id) return;

        var confirm = DialogHelper.Confirm(
            "¿Eliminar este resource pack? Esta acción no se puede deshacer.",
            "Eliminar resource pack");
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            await _packService.RemovePackAsync(id);
            if (SelectedProfile != null) await LoadPacksAsync(SelectedProfile.Id);
            StatusMessage = "Resource pack eliminado.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove resource pack");
            StatusMessage = $"Error al eliminar el resource pack: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
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
