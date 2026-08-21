using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Infrastructure.Services;

public class ScriptService : IScriptService
{
    private readonly IScriptRepository _repository;
    private readonly IProfileRepository _profileRepository;
    private readonly IMinecraftService _minecraftService;
    private readonly ILogger<ScriptService> _logger;
    private readonly ILogService _logService;

    public ScriptService(
        IScriptRepository repository,
        IProfileRepository profileRepository,
        IMinecraftService minecraftService,
        ILogger<ScriptService> logger,
        ILogService logService)
    {
        _repository = repository;
        _profileRepository = profileRepository;
        _minecraftService = minecraftService;
        _logger = logger;
        _logService = logService;
    }

    public async Task<List<Script>> GetScriptsAsync(int profileId) =>
        await _repository.GetByProfileIdAsync(profileId).ConfigureAwait(false);

    public async Task<Script> ImportScriptAsync(int profileId, string sourceFilePath)
    {
        _logger.LogInformation("Importing script from {Source}", sourceFilePath);
        _logService.Info("ScriptService", "ImportScript", $"Importando script {Path.GetFileName(sourceFilePath)}...");

        var profile = await _profileRepository.GetByIdAsync(profileId).ConfigureAwait(false)
            ?? throw new Exception($"Profile {profileId} not found");

        var ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        if (ext is ".exe" or ".bat" or ".cmd" or ".ps1" or ".vbs")
        {
            _logger.LogWarning("Importing potentially executable script: {File}", sourceFilePath);
            _logService.Warning("ScriptService", "ImportScript", $"ADVERTENCIA: '{Path.GetFileName(sourceFilePath)}' es un ejecutable/script del sistema.");
        }

        var gameDir = string.IsNullOrEmpty(profile.GameDirectory)
            ? _minecraftService.GetDefaultGameDirectory(profile.Name)
            : profile.GameDirectory;
        var scriptsDir = Path.Combine(gameDir, "scripts", _minecraftService.SanitizeProfileFolderName(profile.Name));
        Directory.CreateDirectory(scriptsDir);

        var fileName = Path.GetFileName(sourceFilePath);
        var destPath = Path.Combine(scriptsDir, fileName);

        if (File.Exists(destPath))
        {
            var backupPath = await CreateBackupAsync(profileId, destPath).ConfigureAwait(false);
            _logger.LogInformation("Backup created at {Backup}", backupPath);
            _logService.Info("ScriptService", "ImportScript", $"Backup creado: {backupPath}");
        }

        _logService.Info("ScriptService", "ImportScript", $"Copiando {fileName} a scripts...");
        File.Copy(sourceFilePath, destPath, true);

        var script = new Script
        {
            ProfileId = profileId,
            Name = Path.GetFileNameWithoutExtension(fileName),
            FileName = fileName,
            FilePath = destPath,
            Content = await File.ReadAllTextAsync(destPath).ConfigureAwait(false)
        };

        await _repository.CreateAsync(script).ConfigureAwait(false);
        _logService.Info("ScriptService", "ImportScript", $"Script '{script.Name}' importado.");
        return script;
    }

    public async Task<string> ReadScriptContentAsync(int scriptId)
    {
        var script = await _repository.GetByIdAsync(scriptId).ConfigureAwait(false)
            ?? throw new Exception($"Script {scriptId} not found");

        if (File.Exists(script.FilePath))
            script.Content = await File.ReadAllTextAsync(script.FilePath).ConfigureAwait(false);

        return script.Content;
    }

    public async Task SaveScriptContentAsync(int scriptId, string content)
    {
        var script = await _repository.GetByIdAsync(scriptId).ConfigureAwait(false)
            ?? throw new Exception($"Script {scriptId} not found");

        var scriptDir = Path.GetDirectoryName(script.FilePath);
        if (!string.IsNullOrWhiteSpace(scriptDir))
            Directory.CreateDirectory(scriptDir);

        var backupPath = await CreateBackupAsync(script.ProfileId, script.FilePath).ConfigureAwait(false);
        await File.WriteAllTextAsync(script.FilePath, content).ConfigureAwait(false);
        script.Content = content;
        if (!string.IsNullOrEmpty(backupPath))
            script.BackupPath = backupPath;

        await _repository.UpdateAsync(script).ConfigureAwait(false);
        _logger.LogInformation("Script {Name} saved. Backup: {BackupPath}", script.Name, backupPath);

        var message = string.IsNullOrEmpty(backupPath)
            ? $"Script '{script.Name}' guardado (sin backup previo: el archivo no existia)."
            : $"Script '{script.Name}' guardado (backup creado).";
        _logService.Info("ScriptService", "SaveScript", message);
    }

    public async Task DeleteScriptAsync(int scriptId)
    {
        var script = await _repository.GetByIdAsync(scriptId).ConfigureAwait(false)
            ?? throw new Exception($"Script {scriptId} not found");

        try { if (File.Exists(script.FilePath)) File.Delete(script.FilePath); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete script file"); }

        await _repository.DeleteAsync(scriptId).ConfigureAwait(false);
        _logService.Info("ScriptService", "DeleteScript", $"Script '{script.Name}' eliminado.");
    }

    public async Task<string> CreateBackupAsync(int profileId, string filePath)
    {
        if (!File.Exists(filePath)) return string.Empty;

        var profile = await _profileRepository.GetByIdAsync(profileId).ConfigureAwait(false)
            ?? throw new Exception($"Profile {profileId} not found");

        var backupDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShoroCraftLauncher", "backups", _minecraftService.SanitizeProfileFolderName(profile.Name));
        Directory.CreateDirectory(backupDir);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupName = $"{Path.GetFileNameWithoutExtension(filePath)}_{timestamp}{Path.GetExtension(filePath)}.bak";
        var backupPath = Path.Combine(backupDir, backupName);

        File.Copy(filePath, backupPath, true);
        _logger.LogInformation("Backup created: {BackupPath}", backupPath);
        return backupPath;
    }
}
