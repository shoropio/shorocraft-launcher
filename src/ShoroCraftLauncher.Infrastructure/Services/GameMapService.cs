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

    public async Task<List<GameMap>> GetMapsAsync(int profileId)
    {
        var dbMaps = await _repository.GetByProfileIdAsync(profileId);
        var result = new List<GameMap>();

        foreach (var map in dbMaps)
        {
            if (Directory.Exists(map.FilePath))
                result.Add(map);
        }

        var savesDir = await GetMapsFolderAsync(profileId);
        if (Directory.Exists(savesDir))
        {
            var knownFolders = result
                .Select(m => Path.GetFullPath(m.FilePath).TrimEnd('\\', '/'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var worldFolder in Directory.EnumerateDirectories(savesDir))
            {
                if (!File.Exists(Path.Combine(worldFolder, "level.dat"))) continue;

                var full = Path.GetFullPath(worldFolder).TrimEnd('\\', '/');
                if (knownFolders.Contains(full)) continue;

                var iconPath = Path.Combine(worldFolder, "icon.png");
                result.Add(new GameMap
                {
                    Id = 0,
                    ProfileId = profileId,
                    Name = Path.GetFileName(worldFolder),
                    FileName = Path.GetFileName(worldFolder),
                    FilePath = worldFolder,
                    FileSizeBytes = GetFolderSize(worldFolder),
                    PreviewImagePath = File.Exists(iconPath) ? iconPath : null,
                    Status = PackStatus.Active
                });
            }
        }

        return result.OrderBy(m => m.Name).ToList();
    }

    public async Task<GameMap> AddMapAsync(int profileId, string sourceFilePath)
    {
        _logger.LogInformation("Adding map from {Source}", sourceFilePath);
        _logService.Info("GameMapService", "AddMap", $"Agregando mundo {Path.GetFileName(sourceFilePath)}...");

        var ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        if (ext != ".zip" && ext != ".mcworld")
            throw new Exception("Solo se permiten archivos .zip o .mcworld como mapas.");

        var mapsDir = await GetMapsFolderAsync(profileId);
        Directory.CreateDirectory(mapsDir);

        string destDir;
        using (var archive = ZipFile.OpenRead(sourceFilePath))
        {
            var rootLevel = archive.Entries.Any(e => string.Equals(e.FullName, "level.dat", StringComparison.OrdinalIgnoreCase));
            if (rootLevel)
            {
                var baseName = SanitizeFolderName(Path.GetFileNameWithoutExtension(sourceFilePath));
                destDir = Path.Combine(mapsDir, baseName);
                EnsureNotExists(destDir);
                ExtractZipSafe(archive, destDir, string.Empty);
            }
            else
            {
                var levelEntry = archive.Entries
                    .FirstOrDefault(e => e.FullName.EndsWith("/level.dat", StringComparison.OrdinalIgnoreCase)
                                         || (e.FullName.EndsWith("level.dat", StringComparison.OrdinalIgnoreCase)
                                             && e.FullName.Count(c => c == '/') == 1));
                if (levelEntry == null)
                    throw new Exception("El archivo no contiene un mundo válido (falta level.dat).");

                var worldFolder = levelEntry.FullName.Substring(0, levelEntry.FullName.Length - "level.dat".Length).TrimEnd('/');
                if (worldFolder.Contains('\\') || string.IsNullOrEmpty(worldFolder))
                    throw new Exception("El archivo no contiene una estructura de mundo válida.");

                destDir = Path.Combine(mapsDir, worldFolder);
                EnsureNotExists(destDir);
                ExtractZipSafe(archive, destDir, worldFolder + "/");
            }
        }

        var previewPath = ExtractWorldIcon(destDir, mapsDir, Path.GetFileName(destDir));

        var map = new GameMap
        {
            ProfileId = profileId,
            Name = Path.GetFileName(destDir),
            FileName = Path.GetFileName(destDir),
            FilePath = destDir,
            FileSizeBytes = GetFolderSize(destDir),
            PreviewImagePath = string.IsNullOrEmpty(previewPath) ? null : previewPath,
            Status = PackStatus.Active
        };

        await _repository.CreateAsync(map);
        _logService.Info("GameMapService", "AddMap", $"Mundo '{map.Name}' agregado.");
        return map;
    }

    public async Task RemoveMapAsync(GameMap map)
    {
        _logService.Info("GameMapService", "RemoveMap", $"Eliminando mundo '{map.Name}'...");

        try
        {
            if (Directory.Exists(map.FilePath))
                Directory.Delete(map.FilePath, true);
            else if (File.Exists(map.FilePath))
                File.Delete(map.FilePath);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete map folder"); }

        try
        {
            if (!string.IsNullOrEmpty(map.PreviewImagePath)
                && File.Exists(map.PreviewImagePath)
                && map.PreviewImagePath.Contains(".previews", StringComparison.OrdinalIgnoreCase))
                File.Delete(map.PreviewImagePath);
        }
        catch { }

        if (map.Id > 0)
            await _repository.DeleteAsync(map.Id);
        _logService.Info("GameMapService", "RemoveMap", $"Mundo '{map.Name}' eliminado.");
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

    private static void EnsureNotExists(string path)
    {
        if (Directory.Exists(path))
            throw new Exception($"El mundo '{Path.GetFileName(path)}' ya existe.");
    }

    private static void ExtractZipSafe(ZipArchive archive, string destDir, string rootPrefix)
    {
        Directory.CreateDirectory(destDir);
        var destRoot = Path.GetFullPath(destDir).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/')) continue;

            var name = entry.FullName;
            if (!string.IsNullOrEmpty(rootPrefix))
            {
                if (!name.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                name = name.Substring(rootPrefix.Length);
            }
            if (string.IsNullOrEmpty(name)) continue;
            if (name.StartsWith(".previews/", StringComparison.OrdinalIgnoreCase)) continue;

            var target = Path.GetFullPath(Path.Combine(destDir, name));
            if (!target.StartsWith(destRoot, StringComparison.OrdinalIgnoreCase))
                throw new Exception("El archivo contiene rutas no válidas.");

            var parent = Path.GetDirectoryName(target);
            if (parent != null) Directory.CreateDirectory(parent);

            entry.ExtractToFile(target, true);
        }
    }

    private string ExtractWorldIcon(string worldDir, string mapsDir, string worldName)
    {
        try
        {
            var iconInWorld = Path.Combine(worldDir, "icon.png");
            if (File.Exists(iconInWorld)) return iconInWorld;

            var previewDir = Path.Combine(mapsDir, ".previews");
            Directory.CreateDirectory(previewDir);
            var previewPath = Path.Combine(previewDir, $"{SanitizeFolderName(worldName)}.png");

            foreach (var candidate in new[] { "worldicon.png", "icon.png" })
            {
                var src = Path.Combine(worldDir, candidate);
                if (File.Exists(src))
                {
                    File.Copy(src, previewPath, true);
                    return previewPath;
                }
            }
        }
        catch { }

        return string.Empty;
    }

    private static string SanitizeFolderName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "world" : name;
    }

    private static long GetFolderSize(string path)
    {
        try
        {
            return new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
        }
        catch
        {
            return 0;
        }
    }
}
