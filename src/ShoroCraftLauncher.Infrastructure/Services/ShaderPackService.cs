using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Infrastructure.Services;

public class ShaderPackService : IShaderPackService
{
    private readonly IShaderPackRepository _repository;
    private readonly IProfileRepository _profileRepository;
    private readonly IMinecraftService _minecraftService;
    private readonly ILogger<ShaderPackService> _logger;

    public ShaderPackService(
        IShaderPackRepository repository,
        IProfileRepository profileRepository,
        IMinecraftService minecraftService,
        ILogger<ShaderPackService> logger)
    {
        _repository = repository;
        _profileRepository = profileRepository;
        _minecraftService = minecraftService;
        _logger = logger;
    }

    public async Task<List<ShaderPack>> GetPacksAsync(int profileId) =>
        await _repository.GetByProfileIdAsync(profileId);

    public async Task<ShaderPack> AddPackAsync(int profileId, string sourceFilePath)
    {
        _logger.LogInformation("Adding shader pack from {Source}", sourceFilePath);

        var ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        if (ext != ".zip")
            throw new Exception("Solo se permiten archivos .zip como shader packs.");

        var packsDir = await GetPacksFolderAsync(profileId);
        Directory.CreateDirectory(packsDir);

        var fileName = Path.GetFileName(sourceFilePath);
        var destPath = Path.Combine(packsDir, fileName);

        if (File.Exists(destPath))
            throw new Exception($"El shader pack '{fileName}' ya existe.");

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
        return pack;
    }

    public async Task TogglePackAsync(int packId)
    {
        var pack = await _repository.GetByIdAsync(packId)
            ?? throw new Exception($"Shader pack {packId} not found");
        pack.Status = pack.Status == PackStatus.Active ? PackStatus.Inactive : PackStatus.Active;
        await _repository.UpdateAsync(pack);
    }

    public async Task RemovePackAsync(int packId)
    {
        var pack = await _repository.GetByIdAsync(packId)
            ?? throw new Exception($"Shader pack {packId} not found");

        try { if (File.Exists(pack.FilePath)) File.Delete(pack.FilePath); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete shader file"); }

        await _repository.DeleteAsync(packId);
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
        return profile.Type is Core.Enums.ProfileType.OptiFine or Core.Enums.ProfileType.Iris;
    }
}
