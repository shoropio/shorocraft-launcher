using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.App.Commands;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.App.ViewModels;

public class ModsViewModel : BaseViewModel
{
    private readonly IProfileService _profileService;
    private readonly IModService _modService;
    private readonly IProfileRepository _profileRepo;
    private readonly ILogger<ModsViewModel> _logger;

    public ObservableCollection<Mod> Mods { get; } = new();
    public ObservableCollection<Profile> Profiles => _profileService.Profiles;
    public List<string> SearchProviders { get; } = new() { "Modrinth", "CurseForge" };

    public Profile? SelectedProfile
    {
        get => _profileService.SelectedProfile;
        set
        {
            _profileService.SelectedProfile = value;
            OnPropertyChanged(nameof(SelectedProfile));
            if (value != null) _ = LoadModsAsync(value.Id);
        }
    }

    private string _searchQuery = string.Empty;
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            SetProperty(ref _searchQuery, value);
            if (string.IsNullOrEmpty(value) && SelectedProfile != null)
                _ = LoadModsAsync(SelectedProfile.Id);
        }
    }

    public ObservableCollection<Mod> SearchResults { get; } = new();

    private string _selectedProvider = "Modrinth";
    public string SelectedProvider
    {
        get => _selectedProvider;
        set => SetProperty(ref _selectedProvider, value);
    }

    private bool _isSearching;
    public bool IsSearching
    {
        get => _isSearching;
        set => SetProperty(ref _isSearching, value);
    }

    public ICommand AddModCommand { get; }
    public ICommand ToggleModCommand { get; }
    public ICommand RemoveModCommand { get; }
    public ICommand OpenModsFolderCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand SearchCommand { get; }

    public ModsViewModel(
        IProfileService profileService,
        IModService modService,
        IProfileRepository profileRepo,
        ILogger<ModsViewModel> logger)
    {
        _profileService = profileService;
        _modService = modService;
        _profileRepo = profileRepo;
        _logger = logger;

        _profileService.SelectedProfileChanged += () =>
        {
            OnPropertyChanged(nameof(SelectedProfile));
            if (SelectedProfile != null) _ = LoadModsAsync(SelectedProfile.Id);
        };

        AddModCommand = new RelayCommand(async _ => await AddMod());
        ToggleModCommand = new RelayCommand(async p => await ToggleMod(p));
        RemoveModCommand = new RelayCommand(async p => await RemoveMod(p));
        OpenModsFolderCommand = new RelayCommand(async _ => await OpenFolder());
        RefreshCommand = new RelayCommand(async _ => { if (SelectedProfile != null) await LoadModsAsync(SelectedProfile.Id); });
        SearchCommand = new RelayCommand(async _ => await SearchMods());

        if (SelectedProfile != null) _ = LoadModsAsync(SelectedProfile.Id);
    }

    private async Task SearchMods()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery) || SelectedProfile == null) return;

        IsSearching = true;
        IsBusy = true;
        try
        {
            var loader = SelectedProfile.Type.ToString().ToLowerInvariant();
            var results = await _modService.SearchModsAsync(SelectedProvider, SearchQuery, SelectedProfile.MinecraftVersion, loader);
            
            SearchResults.Clear();
            foreach (var r in results)
                SearchResults.Add(r);
            
            StatusMessage = $"Encontrados {SearchResults.Count} mods en {SelectedProvider}.";
        } catch (Exception ex) {
            _logger.LogError(ex, "Search failed");
            StatusMessage = ex.Message;
        }
        IsBusy = false;
    }

    public async Task LoadModsAsync(int profileId)
    {
        IsBusy = true;
        try
        {
            var mods = await _modService.GetModsAsync(profileId);
            Mods.Clear();
            foreach (var m in mods)
                Mods.Add(m);
            StatusMessage = $"{Mods.Count} mods en el perfil.";
        } catch (Exception ex) {
            _logger.LogError(ex, "Failed to load mods");
            StatusMessage = $"Error: {ex.Message}";
        }
        IsBusy = false;
    }

    private async Task AddMod()
    {
        if (SelectedProfile == null) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Mods (*.jar)|*.jar",
            Multiselect = true,
            Title = "Seleccionar mods"
        };

        if (dialog.ShowDialog() == true)
        {
            IsBusy = true;
            var added = 0;
            foreach (var file in dialog.FileNames)
            {
                try
                {
                    await _modService.AddModAsync(SelectedProfile.Id, file);
                    added++;
                } catch (Exception ex) {
                    _logger.LogWarning(ex, "Failed to add mod {File}", file);
                    StatusMessage = $"Error al agregar {Path.GetFileName(file)}: {ex.Message}";
                }
            }
            await LoadModsAsync(SelectedProfile.Id);
            StatusMessage = $"{added} mods agregados.";
            IsBusy = false;
        }
    }

    private async Task ToggleMod(object? modId)
    {
        if (modId is int id)
        {
            await _modService.ToggleModAsync(id);
            if (SelectedProfile != null) {
                await LoadModsAsync(SelectedProfile.Id);
            }
        }
    }

    private async Task RemoveMod(object? modId)
    {
        if (modId is int id)
        {
            await _modService.RemoveModAsync(id);
            if (SelectedProfile != null) {
                await LoadModsAsync(SelectedProfile.Id);
            }
            StatusMessage = "Mod eliminado.";
        }
    }

    private async Task OpenFolder()
    {
        if (SelectedProfile == null) return;
        var folder = await _modService.GetModsFolderAsync(SelectedProfile.Id);
        if (Directory.Exists(folder)) {
            System.Diagnostics.Process.Start("explorer.exe", folder);
        } else {
            StatusMessage = "La carpeta de mods no existe.";
        }
    }
}
