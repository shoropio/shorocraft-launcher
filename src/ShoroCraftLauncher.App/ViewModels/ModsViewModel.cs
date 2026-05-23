using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.App.Commands;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.App.ViewModels;

public class ModsViewModel : BaseViewModel, IDisposable
{
    private readonly IProfileService _profileService;
    private readonly IModService _modService;
    private readonly ILogger<ModsViewModel> _logger;
    private readonly IDialogService _dialogService;

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
            {
                IsSearching = false;
                _ = LoadModsAsync(SelectedProfile.Id);
            }
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
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
    public ICommand InstallFromSearchCommand { get; }

    public ModsViewModel(
        IProfileService profileService,
        IModService modService,
        ILogger<ModsViewModel> logger,
        IDialogService dialogService)
    {
        _profileService = profileService;
        _modService = modService;
        _logger = logger;
        _dialogService = dialogService;

        _profileService.SelectedProfileChanged += OnSelectedProfileChanged;

        AddModCommand = new RelayCommand(async _ => await AddMod(), _ => SelectedProfile != null && !IsBusy);
        ToggleModCommand = new RelayCommand(async p => await ToggleMod(p), _ => SelectedProfile != null && !IsBusy);
        RemoveModCommand = new RelayCommand(async p => await RemoveMod(p), _ => SelectedProfile != null && !IsBusy);
        OpenModsFolderCommand = new RelayCommand(async _ => await OpenFolder(), _ => SelectedProfile != null && !IsBusy);
        RefreshCommand = new RelayCommand(async _ => { if (SelectedProfile != null) await LoadModsAsync(SelectedProfile.Id); }, _ => SelectedProfile != null && !IsBusy);
        SearchCommand = new RelayCommand(async _ => await SearchMods(), _ => SelectedProfile != null && !string.IsNullOrWhiteSpace(SearchQuery) && !IsBusy);
        InstallFromSearchCommand = new RelayCommand(async p => await InstallFromSearch(p), _ => SelectedProfile != null && !IsBusy);

        if (SelectedProfile != null) _ = LoadModsAsync(SelectedProfile.Id);
    }

    private void OnSelectedProfileChanged()
    {
        OnPropertyChanged(nameof(SelectedProfile));
        if (SelectedProfile != null) _ = LoadModsAsync(SelectedProfile.Id);
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }

    public void Dispose()
    {
        _profileService.SelectedProfileChanged -= OnSelectedProfileChanged;
        GC.SuppressFinalize(this);
    }

    private async Task SearchMods()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery) || SelectedProfile == null)
        {
            StatusMessage = "Introduce un término de búsqueda y selecciona un perfil.";
            return;
        }

        IsSearching = true;
        IsBusy = true;
        StatusMessage = "Buscando mods...";

        try
        {
            var loader = SelectedProfile.Type.ToString().ToLowerInvariant();
            var results = await _modService.SearchModsAsync(SelectedProvider, SearchQuery, SelectedProfile.MinecraftVersion, loader);
            
            SearchResults.Clear();
            foreach (var r in results)
                SearchResults.Add(r);
            
            StatusMessage = SearchResults.Count > 0
                ? $"Encontrados {SearchResults.Count} mods en {SelectedProvider}."
                : $"No se encontraron mods en {SelectedProvider}. Prueba otro término.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed");
            StatusMessage = $"Error al buscar mods: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
            IsBusy = false;
        }
    }

    private async Task InstallFromSearch(object? parameter)
    {
        if (SelectedProfile == null)
        {
            StatusMessage = "Selecciona un perfil antes de instalar un mod.";
            return;
        }

        if (parameter is not Mod searchResult)
        {
            StatusMessage = "Selecciona un mod válido para instalar.";
            return;
        }

        if (string.IsNullOrWhiteSpace(searchResult.FileName))
        {
            StatusMessage = "El mod seleccionado no tiene un archivo válido.";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Instalando {searchResult.Name}...";
        try
        {
            await _modService.InstallFromSearchAsync(SelectedProfile.Id, searchResult, SelectedProvider);
            StatusMessage = $"{searchResult.Name} instalado correctamente.";
            IsSearching = false;
            await LoadModsAsync(SelectedProfile.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install mod from search");
            StatusMessage = $"Error al instalar {searchResult.Name}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LoadModsAsync(int profileId)
    {
        IsBusy = true;
        StatusMessage = "Cargando mods...";
        try
        {
            var mods = await _modService.GetModsAsync(profileId);
            Mods.Clear();
            foreach (var m in mods)
                Mods.Add(m);
            StatusMessage = Mods.Count > 0
                ? $"{Mods.Count} mods cargados en el perfil."
                : "No hay mods instalados en este perfil.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load mods");
            StatusMessage = $"Error al cargar mods: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AddMod()
    {
        if (SelectedProfile == null)
        {
            StatusMessage = "Selecciona un perfil antes de agregar mods.";
            return;
        }

        var files = _dialogService.ShowOpenFileDialog("Mods (*.jar)|*.jar", "Seleccionar mods", true);

        if (files == null || files.Length == 0)
        {
            StatusMessage = "No se seleccionó ningún archivo.";
            return;
        }

        IsBusy = true;
        var added = 0;
        try
        {
            foreach (var file in files)
            {
                try
                {
                    await _modService.AddModAsync(SelectedProfile.Id, file);
                    added++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to add mod {File}", file);
                    StatusMessage = $"Error al agregar {Path.GetFileName(file)}: {ex.Message}";
                }
            }
        }
        finally
        {
            await LoadModsAsync(SelectedProfile.Id);
            StatusMessage = added > 0
                ? $"{added} mods agregados."
                : "No se agregó ningún mod.";
            IsBusy = false;
        }
    }

    private async Task ToggleMod(object? modId)
    {
        if (modId is int id)
        {
            IsBusy = true;
            try
            {
                await _modService.ToggleModAsync(id);
                if (SelectedProfile != null) {
                    await LoadModsAsync(SelectedProfile.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to toggle mod {ModId}", id);
                StatusMessage = $"Error al cambiar estado del mod: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    private async Task RemoveMod(object? modId)
    {
        if (modId is not int id) return;

        var confirm = System.Windows.MessageBox.Show(
            "¿Estás seguro de que deseas eliminar este mod?",
            "Eliminar mod",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (confirm != System.Windows.MessageBoxResult.Yes) return;
        IsBusy = true;
        try
        {
            await _modService.RemoveModAsync(id);
            if (SelectedProfile != null)
            {
                await LoadModsAsync(SelectedProfile.Id);
            }
            StatusMessage = "Mod eliminado.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove mod {ModId}", id);
            StatusMessage = $"Error al eliminar el mod: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenFolder()
    {
        if (SelectedProfile == null) return;
        try
        {
            var folder = await _modService.GetModsFolderAsync(SelectedProfile.Id);
            if (Directory.Exists(folder)) {
                System.Diagnostics.Process.Start("explorer.exe", folder);
            } else {
                StatusMessage = "La carpeta de mods no existe.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open mods folder for profile {ProfileId}", SelectedProfile.Id);
            StatusMessage = $"Error al abrir la carpeta: {ex.Message}";
        }
    }
}
