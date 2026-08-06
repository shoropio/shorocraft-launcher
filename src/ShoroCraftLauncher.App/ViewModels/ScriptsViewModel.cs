using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.App.Commands;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.App.ViewModels;

public class ScriptsViewModel : BaseViewModel
{
    private readonly IScriptService _scriptService;
    private readonly ILogger<ScriptsViewModel> _logger;
    private readonly IDialogService _dialogService;

    public ObservableCollection<Script> Scripts { get; } = new();
    public ObservableCollection<Profile> Profiles => _profileService.Profiles;

    private readonly IProfileService _profileService;

    private Profile? _selectedProfile;
    public Profile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            SetProperty(ref _selectedProfile, value);
            if (value != null) _ = LoadScriptsAsync(value.Id);
        }
    }

    private Script? _selectedScript;
    public Script? SelectedScript
    {
        get => _selectedScript;
        set
        {
            SetProperty(ref _selectedScript, value);
            if (value != null) _ = LoadScriptContentAsync(value.Id);
        }
    }

    private string _scriptContent = string.Empty;
    public string ScriptContent
    {
        get => _scriptContent;
        set => SetProperty(ref _scriptContent, value);
    }

    public ICommand ImportScriptCommand { get; }
    public ICommand SaveScriptCommand { get; }
    public ICommand DeleteScriptCommand { get; }

    public ScriptsViewModel(
        IScriptService scriptService,
        IProfileService profileService,
        IDialogService dialogService,
        ILogger<ScriptsViewModel> logger)
    {
        _scriptService = scriptService;
        _profileService = profileService;
        _dialogService = dialogService;
        _logger = logger;

        ImportScriptCommand = new RelayCommand(async _ => await ImportScript());
        SaveScriptCommand = new RelayCommand(async _ => await SaveScript(), _ => SelectedScript != null);
        DeleteScriptCommand = new RelayCommand(async _ => await DeleteScript(), _ => SelectedScript != null);

        SelectedProfile = _profileService.SelectedProfile ?? Profiles.FirstOrDefault();
    }

    private async Task LoadScriptsAsync(int profileId)
    {
        IsBusy = true;
        try
        {
            var scripts = await _scriptService.GetScriptsAsync(profileId);
            Scripts.Clear();
            foreach (var s in scripts) Scripts.Add(s);
            StatusMessage = $"{Scripts.Count} scripts.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load scripts");
            StatusMessage = $"Error: {ex.Message}";
        }
        IsBusy = false;
    }

    private async Task LoadScriptContentAsync(int scriptId)
    {
        try
        {
            ScriptContent = await _scriptService.ReadScriptContentAsync(scriptId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load script content");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    private async Task ImportScript()
    {
        if (SelectedProfile == null) return;
        var files = _dialogService.ShowOpenFileDialog(
            "Todos los archivos (*.*)|*.*",
            "Seleccionar archivo de configuración/script",
            multiselect: false);
        if (files == null) return;

        IsBusy = true;
        try
        {
            await _scriptService.ImportScriptAsync(SelectedProfile.Id, files[0]);
            await LoadScriptsAsync(SelectedProfile.Id);
            StatusMessage = "Script importado.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        IsBusy = false;
    }

    private async Task SaveScript()
    {
        if (SelectedScript == null) return;
        IsBusy = true;
        try
        {
            await _scriptService.SaveScriptContentAsync(SelectedScript.Id, ScriptContent);
            StatusMessage = "Script guardado.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        IsBusy = false;
    }

    private async Task DeleteScript()
    {
        if (SelectedScript == null) return;
        IsBusy = true;
        try
        {
            await _scriptService.DeleteScriptAsync(SelectedScript.Id);
            ScriptContent = string.Empty;
            if (SelectedProfile != null) await LoadScriptsAsync(SelectedProfile.Id);
            StatusMessage = "Script eliminado.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        IsBusy = false;
    }
}
