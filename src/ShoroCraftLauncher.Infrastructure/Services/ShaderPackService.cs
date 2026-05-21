using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Infrastructure.Services;

public class ShaderPackService : IShaderPackService
{
    private readonly IShaderPackRepository _repository;
    private readonly IProfileRepository _profileRepository;
    private readonly IModRepository _modRepository;
    private readonly IMinecraftService _minecraftService;
    private readonly ILogger<ShaderPackService> _logger;
    private readonly ILogService _logService;

    public ShaderPackService(
        IShaderPackRepository repository,
        IProfileRepository profileRepository,
        IModRepository modRepository,
        IMinecraftService minecraftService,
        ILogger<ShaderPackService> logger,
        ILogService logService)
    {
        _repository = repository;
        _profileRepository = profileRepository;
        _modRepository = modRepository;
        _minecraftService = minecraftService;
        _logger = logger;
        _logService = logService;
    }

    public async Task<List<ShaderPack>> GetPacksAsync(int profileId) =>
        await _repository.GetByProfileIdAsync(profileId);

    public async Task<ShaderPack> AddPackAsync(int profileId, string sourceFilePath)
    {
        _logger.LogInformation("Adding shader pack from {Source}", sourceFilePath);
        _logService.Info("ShaderPackService", "AddPack", $"Agregando shader {Path.GetFileName(sourceFilePath)}...");

        var ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        if (ext != ".zip")
            throw new Exception("Solo se permiten archivos .zip como shader packs.");

        var packsDir = await GetPacksFolderAsync(profileId);
        Directory.CreateDirectory(packsDir);

        var fileName = Path.GetFileName(sourceFilePath);
        var destPath = Path.Combine(packsDir, fileName);

        if (File.Exists(destPath))
            throw new Exception($"El shader pack '{fileName}' ya existe.");

        _logService.Info("ShaderPackService", "AddPack", $"Copiando {fileName} a shaderpacks...");
        File.Copy(sourceFilePath, destPath);

        var pack = new ShaderPack
        {
            ProfileId = profileId,
            Name = Path.GetFileNameWithoutExtension(fileName),
            FileName = fileName,
            FilePath = destPath,
            FileSizeBytes = new FileInfo(destPath).Length,
            Status = PackStatus.Active
        };

        await _repository.CreateAsync(pack);
        _logService.Info("ShaderPackService", "AddPack", $"Shader '{pack.Name}' agregado.");
        return pack;
    }

    public async Task TogglePackAsync(int packId)
    {
        var pack = await _repository.GetByIdAsync(packId)
            ?? throw new Exception($"Shader pack {packId} not found");
        pack.Status = pack.Status == PackStatus.Active ? PackStatus.Inactive : PackStatus.Active;
        await _repository.UpdateAsync(pack);
        _logService.Info("ShaderPackService", "TogglePack", $"Shader '{pack.Name}' {(pack.Status == PackStatus.Active ? "activado" : "desactivado")}.");
    }

    public async Task RemovePackAsync(int packId)
    {
        var pack = await _repository.GetByIdAsync(packId)
            ?? throw new Exception($"Shader pack {packId} not found");

        _logService.Info("ShaderPackService", "RemovePack", $"Eliminando shader '{pack.Name}'...");
        try { if (File.Exists(pack.FilePath)) File.Delete(pack.FilePath); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete shader file"); }

        await _repository.DeleteAsync(packId);
        _logService.Info("ShaderPackService", "RemovePack", $"Shader '{pack.Name}' eliminado.");
    }

    public async Task<string> GetPacksFolderAsync(int profileId)
    {
        var profile = await _profileRepository.GetByIdAsync(profileId)
            ?? throw new Exception($"Profile {profileId} not found");
        var gameDir = string.IsNullOrEmpty(profile.GameDirectory)
            ? _minecraftService.GetDefaultGameDirectory(profile.Name)
            : profile.GameDirectory;
        return _minecraftService.GetShaderPacksDirectory(gameDir);
    }

    public async Task<bool> HasShaderSupportAsync(int profileId)
    {
        var profile = await _profileRepository.GetByIdAsync(profileId)
            ?? throw new Exception($"Profile {profileId} not found");

        if (profile.Type is ProfileType.OptiFine or ProfileType.Iris)
            return true;

        var mods = await _modRepository.GetByProfileIdAsync(profileId);
        if (mods.Any(IsActiveShaderMod))
            return true;

        var gameDir = string.IsNullOrEmpty(profile.GameDirectory)
            ? _minecraftService.GetDefaultGameDirectory(profile.Name)
            : profile.GameDirectory;
        var modsDir = _minecraftService.GetModsDirectory(gameDir);

        if (!Directory.Exists(modsDir))
            return false;

        return Directory.GetFiles(modsDir, "*.jar")
            .Select(Path.GetFileNameWithoutExtension)
            .Any(IsShaderModName);
    }

    private static bool IsActiveShaderMod(Mod mod)
    {
        if (mod.Status != ModStatus.Active)
            return false;

        return IsShaderModName(mod.Name)
            || IsShaderModName(mod.FileName)
            || IsShaderModName(mod.ModVersion);
    }

    private static bool IsShaderModName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Contains("iris", StringComparison.OrdinalIgnoreCase)
            || value.Contains("oculus", StringComparison.OrdinalIgnoreCase)
            || value.Contains("optifine", StringComparison.OrdinalIgnoreCase);
    }
}
