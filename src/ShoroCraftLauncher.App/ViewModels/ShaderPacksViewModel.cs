using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.App.Commands;
using ShoroCraftLauncher.App.Services;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.App.ViewModels;

public class ShaderPacksViewModel : BaseViewModel, IDisposable
{
    private readonly IShaderPackService _packService;
    private readonly ILogger<ShaderPacksViewModel> _logger;
    private readonly IDialogService _dialogService;

    public ObservableCollection<ShaderPack> Packs { get; } = new();
    public ObservableCollection<ShaderPackSearchResult> SearchResults { get; } = new();
    public ObservableCollection<Profile> Profiles => _profileService.Profiles;

    private readonly IProfileService _profileService;

    public Profile? SelectedProfile
    {
        get => _profileService.SelectedProfile;
        set
        {
            _profileService.SelectedProfile = value;
            OnPropertyChanged(nameof(SelectedProfile));
            if (value != null) _ = LoadPacksAsync(value.Id, loadPopular: true);
        }
    }

    private string _searchQuery = string.Empty;
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            SetProperty(ref _searchQuery, value);
            if (string.IsNullOrEmpty(value))
                ShowSearchResults = false;
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    private bool _showSearchResults;
    public bool ShowSearchResults
    {
        get => _showSearchResults;
        set => SetProperty(ref _showSearchResults, value);
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
    public ICommand SearchCommand { get; }
    public ICommand ExplorePopularCommand { get; }
    public ICommand InstallFromSearchCommand { get; }
    public ICommand ShowInstalledCommand { get; }
    public ICommand ShowRecommendedCommand { get; }

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

        _profileService.SelectedProfileChanged += OnSelectedProfileChanged;
        AddPackCommand = new RelayCommand(async _ => await AddPack());
        TogglePackCommand = new RelayCommand(async p => await TogglePack(p));
        RemovePackCommand = new RelayCommand(async p => await RemovePack(p));
        OpenFolderCommand = new RelayCommand(async _ => await OpenFolder());
        RefreshCommand = new RelayCommand(async _ => { if (SelectedProfile != null) await LoadPacksAsync(SelectedProfile.Id); });
        SearchCommand = new RelayCommand(async _ => await SearchShaders(), _ => SelectedProfile != null && !string.IsNullOrWhiteSpace(SearchQuery) && !IsBusy);
        ExplorePopularCommand = new RelayCommand(async _ => await SearchShaders(), _ => SelectedProfile != null && !IsBusy);
        InstallFromSearchCommand = new RelayCommand(async p => await InstallFromSearch(p), _ => SelectedProfile != null && !IsBusy);
        ShowInstalledCommand = new RelayCommand(async _ => { ShowSearchResults = false; if (SelectedProfile != null) await LoadPacksAsync(SelectedProfile.Id); }, _ => SelectedProfile != null && !IsBusy);
        ShowRecommendedCommand = new RelayCommand(async _ => await LoadRecommended(), _ => SelectedProfile != null && !IsBusy);

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

    private async Task LoadPacksAsync(int profileId, bool loadPopular = false)
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

            HasShaderSupport = await _packService.HasShaderSupportAsync(profileId);
            if (!HasShaderSupport)
                StatusMessage = "Este perfil no tiene soporte de shaders. Usa OptiFine o Iris.";
            else
            {
                StatusMessage = $"{Packs.Count} shader packs.";
                if (loadPopular) _ = SearchShaders();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load shaders");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SearchShaders()
    {
        if (SelectedProfile == null) return;

        ShowSearchResults = true;
        IsBusy = true;
        StatusMessage = string.IsNullOrWhiteSpace(SearchQuery) ? "Cargando shader packs populares..." : "Buscando shaders...";

        try
        {
            var results = await _packService.SearchShadersAsync(SearchQuery, SelectedProfile.MinecraftVersion);
            SearchResults.Clear();
            foreach (var r in results) SearchResults.Add(r);
            StatusMessage = SearchResults.Count > 0
                ? $"Encontrados {SearchResults.Count} shader packs."
                : "No se encontraron shaders. Prueba otro término.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search shaders");
            StatusMessage = $"Error al buscar shaders: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadRecommended()
    {
        if (SelectedProfile == null) return;

        IsBusy = true;
        StatusMessage = "Cargando shader packs recomendados...";
        try
        {
            var packs = await _packService.GetRecommendedShadersAsync();
            SearchResults.Clear();
            foreach (var p in packs) SearchResults.Add(p);
            ShowSearchResults = true;
            StatusMessage = $"{SearchResults.Count} shader packs recomendados.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load recommended shaders");
            StatusMessage = $"Error al cargar recomendados: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task InstallFromSearch(object? parameter)
    {
        if (SelectedProfile == null || parameter is not ShaderPackSearchResult searchResult)
        {
            StatusMessage = "Selecciona un shader válido para instalar.";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Instalando {searchResult.Name}...";
        try
        {
            await _packService.InstallFromSearchAsync(SelectedProfile.Id, searchResult);
            StatusMessage = $"{searchResult.Name} instalado correctamente.";
            ShowSearchResults = false;
            await LoadPacksAsync(SelectedProfile.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install shader from search");
            StatusMessage = $"Error al instalar {searchResult.Name}: {ex.Message}";
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
        if (packId is not int id) return;

        var confirm = DialogHelper.Confirm(
            "¿Eliminar este shader pack? Esta acción no se puede deshacer.",
            "Eliminar shader pack");
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            await _packService.RemovePackAsync(id);
            if (SelectedProfile != null) await LoadPacksAsync(SelectedProfile.Id);
            StatusMessage = "Shader pack eliminado.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove shader pack");
            StatusMessage = $"Error al eliminar el shader pack: {ex.Message}";
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
