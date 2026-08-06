using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.App.Commands;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.App.ViewModels;

public class ShaderPacksViewModel : BaseViewModel
{
    private readonly IShaderPackService _packService;
    private readonly ILogger<ShaderPacksViewModel> _logger;
    private readonly IDialogService _dialogService;

    public ObservableCollection<ShaderPack> Packs { get; } = new();
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

    private bool _hasShaderSupport;
    public bool HasShaderSupport
    {
        get => _hasShaderSupport;
        set => SetProperty(ref _hasShaderSupport, value);
    }

    public ICommand AddPackCommand { get; }
    public ICommand TogglePackCommand { get; }
    public ICommand RemovePackCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand RefreshCommand { get; }

    public ShaderPacksViewModel(
        IShaderPackService packService,
        IProfileService profileService,
        IDialogService dialogService,
        ILogger<ShaderPacksViewModel> logger)
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

            HasShaderSupport = await _packService.HasShaderSupportAsync(profileId);
            if (!HasShaderSupport)
                StatusMessage = "Este perfil no tiene soporte de shaders. Usa OptiFine o Iris.";
            else
                StatusMessage = $"{Packs.Count} shader packs.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load shaders");
            StatusMessage = $"Error: {ex.Message}";
        }
        IsBusy = false;
    }

    private async Task AddPack()
    {
        if (SelectedProfile == null) return;
        var files = _dialogService.ShowOpenFileDialog(
            "Shader Packs (*.zip)|*.zip",
            "Seleccionar shader packs",
            multiselect: true);
        if (files == null) return;

        IsBusy = true;
        var added = 0;
        foreach (var file in files)
        {
            try { await _packService.AddPackAsync(SelectedProfile.Id, file); added++; }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to add shader"); }
        }
        await LoadPacksAsync(SelectedProfile.Id);
        StatusMessage = $"{added} shaders agregados.";
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
