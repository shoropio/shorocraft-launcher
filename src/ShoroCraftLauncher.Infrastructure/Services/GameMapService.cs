using System.IO.Compression;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Infrastructure.Services;

public class GameMapService : IGameMapService
{
    private readonly IGameMapRepository _repository;
    private readonly IProfileRepository _profileRepository;
    private readonly IMinecraftService _minecraftService;
    private readonly ILogger<GameMapService> _logger;
    private readonly ILogService _logService;

    public GameMapService(
        IGameMapRepository repository,
        IProfileRepository profileRepository,
        IMinecraftService minecraftService,
        ILogger<GameMapService> logger,
        ILogService logService)
    {
        _repository = repository;
        _profileRepository = profileRepository;
        _minecraftService = minecraftService;
        _logger = logger;
        _logService = logService;
    }

    public async Task<List<GameMap>> GetMapsAsync(int profileId) =>
        await _repository.GetByProfileIdAsync(profileId);

    public async Task<GameMap> AddMapAsync(int profileId, string sourceFilePath)
    {
        _logger.LogInformation("Adding map from {Source}", sourceFilePath);
        _logService.Info("GameMapService", "AddMap", $"Agregando mapa {Path.GetFileName(sourceFilePath)}...");

        var ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        if (ext != ".zip" && ext != ".mcworld")
            throw new Exception("Solo se permiten archivos .zip o .mcworld como mapas.");

        var mapsDir = await GetMapsFolderAsync(profileId);
        Directory.CreateDirectory(mapsDir);

        var fileName = Path.GetFileName(sourceFilePath);
        var destPath = Path.Combine(mapsDir, fileName);

        if (File.Exists(destPath))
            throw new Exception($"El mapa '{fileName}' ya existe.");

        _logService.Info("GameMapService", "AddMap", $"Copiando {fileName} a saves...");
        File.Copy(sourceFilePath, destPath, false);

        var previewPath = await ExtractPreviewImageAsync(destPath, mapsDir);

        var map = new GameMap
        {
            ProfileId = profileId,
            Name = Path.GetFileNameWithoutExtension(fileName),
            FileName = fileName,
            FilePath = destPath,
            FileSizeBytes = new FileInfo(destPath).Length,
            PreviewImagePath = previewPath,
            Status = PackStatus.Active
        };

        await _repository.CreateAsync(map);
        _logService.Info("GameMapService", "AddMap", $"Mapa '{map.Name}' agregado.");
        return map;
    }

    public async Task RemoveMapAsync(int mapId)
    {
        var map = await _repository.GetByIdAsync(mapId)
            ?? throw new Exception($"Map {mapId} not found");

        _logService.Info("GameMapService", "RemoveMap", $"Eliminando mapa '{map.Name}'...");
        try { if (File.Exists(map.FilePath)) File.Delete(map.FilePath); }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete map file"); }

        try
        {
            if (!string.IsNullOrEmpty(map.PreviewImagePath) && File.Exists(map.PreviewImagePath))
                File.Delete(map.PreviewImagePath);
        }
        catch { }

        await _repository.DeleteAsync(mapId);
        _logService.Info("GameMapService", "RemoveMap", $"Mapa '{map.Name}' eliminado.");
    }

    public async Task<string> GetMapsFolderAsync(int profileId)
    {
        var profile = await _profileRepository.GetByIdAsync(profileId)
            ?? throw new Exception($"Profile {profileId} not found");
        var gameDir = string.IsNullOrEmpty(profile.GameDirectory)
            ? _minecraftService.GetDefaultGameDirectory(profile.Name)
            : profile.GameDirectory;
        return _minecraftService.GetSavesDirectory(gameDir);
    }

    private async Task<string> ExtractPreviewImageAsync(string zipPath, string mapsDir)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var entry = archive.GetEntry("worldicon.png") ?? archive.GetEntry("icon.png");
            if (entry == null) return string.Empty;

            var previewDir = Path.Combine(mapsDir, ".previews");
            Directory.CreateDirectory(previewDir);
            var previewPath = Path.Combine(previewDir, $"{Path.GetFileNameWithoutExtension(zipPath)}.png");

            entry.ExtractToFile(previewPath, true);
            return previewPath;
        }
        catch
        {
            return string.Empty;
        }
    }
}
