using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.Core;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Infrastructure.Downloading;

namespace ShoroCraftLauncher.Infrastructure.Services;


public partial class ModService : IModService
{
    private const string UserAgent = "ShoroCraftLauncher/1.6.5 (https://github.com/Shoropio/shorocraft-launcher)";
    private readonly IModRepository _modRepository;
    private readonly IProfileRepository _profileRepository;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IGameDirectories _gameDirectories;
    private readonly ModSearchService _modSearchService;
    private readonly ILogger<ModService> _logger;
    private readonly ILogService _logService;
    private readonly HttpClient _httpClient;
    private readonly IResumableDownloadService _resumableDownloadService;

    public ModService(
        IModRepository modRepository,
        IProfileRepository profileRepository,
        ISettingsRepository settingsRepository,
        IGameDirectories gameDirectories,
        ModSearchService modSearchService,
        ILogger<ModService> logger,
        ILogService logService,
        HttpClient httpClient,
        IResumableDownloadService? resumableDownloadService = null)
    {
        _modRepository = modRepository;
        _profileRepository = profileRepository;
        _settingsRepository = settingsRepository;
        _gameDirectories = gameDirectories;
        _modSearchService = modSearchService;
        _logger = logger;
        _logService = logService;
        _httpClient = httpClient;
        _resumableDownloadService = resumableDownloadService ?? new ResumableDownloadService(httpClient);
    }

    public async Task<List<Mod>> SearchModsAsync(string provider, string query, string minecraftVersion, string loaderType)
    {
        return await _modSearchService.SearchModsAsync(provider, query, minecraftVersion, loaderType).ConfigureAwait(false);
    }

    public async Task<List<Mod>> SearchModrinthAsync(string query, string minecraftVersion, string loaderType)
    {
        return await _modSearchService.SearchModrinthAsync(query, minecraftVersion, loaderType).ConfigureAwait(false);
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }

    public async Task<List<Mod>> GetModsAsync(int profileId) =>
        await _modRepository.GetByProfileIdAsync(profileId).ConfigureAwait(false);

    public async Task<Mod> AddModAsync(int profileId, string sourceFilePath)
    {
        _logger.LogInformation("Adding mod from {Source} to profile {ProfileId}", sourceFilePath, profileId);
        _logService.Info("ModService", "AddMod", $"Agregando mod desde {Path.GetFileName(sourceFilePath)}...");

        var profile = await _profileRepository.GetByIdAsync(profileId).ConfigureAwait(false)
            ?? throw new Exception($"Profile {profileId} not found");

        var extension = Path.GetExtension(sourceFilePath).ToLowerInvariant();
        if (extension != ".jar")
            throw new Exception("Solo se permiten archivos .jar como mods.");

        var modsDir = await GetModsFolderAsync(profileId).ConfigureAwait(false);
        Directory.CreateDirectory(modsDir);

        var fileName = Path.GetFileName(sourceFilePath);
        var destPath = Path.Combine(modsDir, fileName);

        if (File.Exists(destPath) || File.Exists(destPath + ".disabled"))
            throw new Exception($"El mod '{fileName}' ya existe en este perfil.");

        File.Copy(sourceFilePath, destPath, false);

        var modInfo = await ExtractModInfoAsync(destPath).ConfigureAwait(false);
        var mod = new Mod
        {
            ProfileId = profileId,
            Name = modInfo.Name ?? Path.GetFileNameWithoutExtension(fileName),
            FileName = fileName,
            FilePath = destPath,
            FileSizeBytes = new FileInfo(destPath).Length,
            MinecraftVersion = modInfo.MinecraftVersion ?? profile.MinecraftVersion,
            ModVersion = modInfo.ModVersion ?? "unknown",
            Status = ModStatus.Active
        };

        await _modRepository.CreateAsync(mod).ConfigureAwait(false);
        _logger.LogInformation("Mod {Name} added successfully", mod.Name);
        _logService.Info("ModService", "AddMod", $"Mod '{mod.Name}' agregado.");
        return mod;
    }

    public async Task ToggleModAsync(int modId)
    {
        var mod = await _modRepository.GetByIdAsync(modId).ConfigureAwait(false)
            ?? throw new Exception($"Mod {modId} not found");

        if (mod.Status == ModStatus.Active)
        {
            var disabledPath = mod.FilePath + ".disabled";
            if (File.Exists(mod.FilePath))
            {
                File.Move(mod.FilePath, disabledPath);
                mod.FilePath = disabledPath;
            }
            mod.Status = ModStatus.Inactive;
        }
        else
        {
            var activePath = mod.FilePath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                ? mod.FilePath[..^".disabled".Length]
                : mod.FilePath;
            if (!string.Equals(mod.FilePath, activePath, StringComparison.OrdinalIgnoreCase)
                && File.Exists(mod.FilePath))
            {
                File.Move(mod.FilePath, activePath);
                mod.FilePath = activePath;
            }
            mod.Status = ModStatus.Active;
        }

        await _modRepository.UpdateAsync(mod).ConfigureAwait(false);
        _logger.LogInformation("Mod {Name} toggled to {Status}", mod.Name, mod.Status);
        _logService.Info("ModService", "ToggleMod", $"Mod '{mod.Name}' {(mod.Status == ModStatus.Active ? "activado" : "desactivado")}.");
    }

    public async Task RemoveModAsync(int modId)
    {
        var mod = await _modRepository.GetByIdAsync(modId).ConfigureAwait(false)
            ?? throw new Exception($"Mod {modId} not found");

        try
        {
            if (File.Exists(mod.FilePath))
                File.Delete(mod.FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete mod file {Path}", mod.FilePath);
        }

        await _modRepository.DeleteAsync(modId).ConfigureAwait(false);
        _logger.LogInformation("Mod {Name} removed", mod.Name);
        _logService.Info("ModService", "RemoveMod", $"Mod '{mod.Name}' eliminado.");
    }

    public async Task<string> GetModsFolderAsync(int profileId)
    {
        var profile = await _profileRepository.GetByIdAsync(profileId).ConfigureAwait(false)
            ?? throw new Exception($"Profile {profileId} not found");
        var gameDir = string.IsNullOrEmpty(profile.GameDirectory)
            ? _gameDirectories.GetDefaultGameDirectory(profile.Name)
            : profile.GameDirectory;
        return _gameDirectories.GetModsDirectory(gameDir);
    }

    public async Task<(string? Name, string? MinecraftVersion, string? ModVersion)> ExtractModInfoAsync(string jarPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jarPath);
            var mcmodEntry = archive.GetEntry("mcmod.info");
            if (mcmodEntry != null)
            {
                using var reader = new StreamReader(mcmodEntry.Open(), Encoding.UTF8);
                var content = await reader.ReadToEndAsync().ConfigureAwait(false);
                return ParseMcmodInfo(content);
            }

            var fabricEntry = archive.Entries.FirstOrDefault(e =>
                e.FullName.StartsWith("fabric.mod.json", StringComparison.OrdinalIgnoreCase));
            if (fabricEntry != null)
            {
                using var reader = new StreamReader(fabricEntry.Open(), Encoding.UTF8);
                var content = await reader.ReadToEndAsync().ConfigureAwait(false);
                return ParseFabricModJson(content);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract mod info from {Jar}", jarPath);
        }

        return (null, null, null);
    }

    private (string? Name, string? MinecraftVersion, string? ModVersion) ParseMcmodInfo(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind == System.Text.Json.JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                var first = root[0];
                return (
                    first.TryGetProperty("name", out var n) ? n.GetString() : null,
                    first.TryGetProperty("mcversion", out var mv) ? mv.GetString() : null,
                    first.TryGetProperty("version", out var v) ? v.GetString() : null
                );
            }
        }
        catch { }
        return (null, null, null);
    }

    private (string? Name, string? MinecraftVersion, string? ModVersion) ParseFabricModJson(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;

            string? mcVersion = null;
            if (root.TryGetProperty("depends", out var depends))
            {
                if (depends.TryGetProperty("minecraft", out var mc))
                    mcVersion = mc.GetString();
            }

            string? modVersion = null;
            if (root.TryGetProperty("version", out var v))
                modVersion = v.GetString();

            return (name, mcVersion, modVersion);
        }
        catch { }
        return (null, null, null);
    }

}
