using System.IO.Compression;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Infrastructure.Services;

public class ResourcePackService : IResourcePackService
{
    private readonly IResourcePackRepository _repository;
    private readonly IProfileRepository _profileRepository;
    private readonly IMinecraftService _minecraftService;
    private readonly ILogger<ResourcePackService> _logger;

    public ResourcePackService(
        IResourcePackRepository repository,
        IProfileRepository profileRepository,
        IMinecraftService minecraftService,
        ILogger<ResourcePackService> logger)
    {
        _repository = repository;
        _profileRepository = profileRepository;
        _minecraftService = minecraftService;
        _logger = logger;
    }

    public async Task<List<ResourcePack>> GetPacksAsync(int profileId) =>
        await _repository.GetByProfileIdAsync(profileId);

    public async Task<ResourcePack> AddPackAsync(int profileId, string sourceFilePath)
    {
        _logger.LogInformation("Adding resource pack from {Source}", sourceFilePath);

        var ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        if (ext != ".zip")
            throw new Exception("Solo se permiten archivos .zip como resource packs.");

        var packsDir = await GetPacksFolderAsync(profileId);
        Directory.CreateDirectory(packsDir);

        var fileName = Path.GetFileName(sourceFilePath);
        var destPath = Path.Combine(packsDir, fileName);

        if (File.Exists(destPath))
            throw new Exception($"El resource pack '{fileName}' ya existe.");

        File.Copy(sourceFilePath, destPath);

        var previewPath = await ExtractPreviewImageAsync(destPath, packsDir);

        var pack = new ResourcePack
        {
            ProfileId = profileId,
            Name = Path.GetFileNameWithoutExtension(fileName),
            FileName = fileName,
            FilePath = destPath,
            FileSizeBytes = new FileInfo(destPath).Length,
            PreviewImagePath = previewPath,
            Status = PackStatus.Active
        };

        await _repository.CreateAsync(pack);
        return pack;
    }

    public async Task TogglePackAsync(int packId)
    {
        var pack = await _repository.GetByIdAsync(packId)
            ?? throw new Exception($"Resource pack {packId} not found");
        pack.Status = pack.Status == PackStatus.Active ? PackStatus.Inactive : PackStatus.Active;
        await _repository.UpdateAsync(pack);
    }

    public async Task RemovePackAsync(int packId)
    {
        var pack = await _repository.GetByIdAsync(packId)
            ?? throw new Exception($"Resource pack {packId} not found");

        try { if (File.Exists(pack.FilePath)) File.Delete(pack.FilePath); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete pack file"); }

        try
        {
            if (!string.IsNullOrEmpty(pack.PreviewImagePath) && File.Exists(pack.PreviewImagePath))
                File.Delete(pack.PreviewImagePath);
        }
        catch { }

        await _repository.DeleteAsync(packId);
    }

    public async Task<string> GetPacksFolderAsync(int profileId)
    {
        var profile = await _profileRepository.GetByIdAsync(profileId)
            ?? throw new Exception($"Profile {profileId} not found");
        var gameDir = string.IsNullOrEmpty(profile.GameDirectory)
            ? _minecraftService.GetDefaultGameDirectory(profile.Name)
            : profile.GameDirectory;
        return _minecraftService.GetResourcePacksDirectory(gameDir);
    }

    private async Task<string> ExtractPreviewImageAsync(string zipPath, string packsDir)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var packEntry = archive.GetEntry("pack.png");
            if (packEntry == null) return string.Empty;

            var previewDir = Path.Combine(packsDir, ".previews");
            Directory.CreateDirectory(previewDir);
            var previewPath = Path.Combine(previewDir, $"{Path.GetFileNameWithoutExtension(zipPath)}.png");

            packEntry.ExtractToFile(previewPath, true);
            return previewPath;
        }
        catch
        {
            return string.Empty;
        }
    }
}
