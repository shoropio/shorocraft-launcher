using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Infrastructure.Services;

public class ModpackService : IModpackService
{
    private readonly IModRepository _modRepository;
    private readonly IProfileRepository _profileRepository;
    private readonly IMinecraftService _minecraftService;
    private readonly ILogService _logService;
    private readonly ILogger<ModpackService> _logger;
    private readonly HttpClient _httpClient;

    public ModpackService(
        IModRepository modRepository,
        IProfileRepository profileRepository,
        IMinecraftService minecraftService,
        ILogService logService,
        ILogger<ModpackService> logger,
        HttpClient httpClient)
    {
        _modRepository = modRepository;
        _profileRepository = profileRepository;
        _minecraftService = minecraftService;
        _logService = logService;
        _logger = logger;
        _httpClient = httpClient;
    }

    public async Task<ModpackImportResult> ImportFromFileAsync(int profileId, string mrpackPath, Action<string>? onProgress = null)
    {
        if (Path.GetExtension(mrpackPath).ToLowerInvariant() != ".mrpack")
            throw new Exception("Solo se permiten archivos .mrpack (modpacks de Modrinth).");

        return await ImportCoreAsync(profileId, mrpackPath, onProgress);
    }

    public async Task<ModpackImportResult> ImportFromUrlAsync(int profileId, string url, Action<string>? onProgress = null)
    {
        onProgress?.Invoke("Descargando modpack...");
        var tempFile = Path.Combine(Path.GetTempPath(), $"mrpack_{Guid.NewGuid():N}.mrpack");
        try
        {
            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync();
            await using var output = File.Create(tempFile);
            await input.CopyToAsync(output);
            return await ImportCoreAsync(profileId, tempFile, onProgress);
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
        }
    }

    private async Task<ModpackImportResult> ImportCoreAsync(int profileId, string mrpackPath, Action<string>? onProgress)
    {
        var profile = await _profileRepository.GetByIdAsync(profileId)
            ?? throw new Exception($"Perfil {profileId} no encontrado.");

        var gameDir = string.IsNullOrEmpty(profile.GameDirectory)
            ? _minecraftService.GetDefaultGameDirectory(profile.Name)
            : profile.GameDirectory;

        var tempDir = Path.Combine(Path.GetTempPath(), $"mrpack_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            _logService.Info("ModpackService", "Import", $"Extrayendo modpack {Path.GetFileName(mrpackPath)}...");
            ZipFile.ExtractToDirectory(mrpackPath, tempDir);

            var indexPath = Path.Combine(tempDir, "modrinth.index.json");
            if (!File.Exists(indexPath))
                throw new Exception("El modpack no contiene modrinth.index.json.");

            var index = JsonSerializer.Deserialize<MrpackIndex>(await File.ReadAllTextAsync(indexPath))
                ?? throw new Exception("No se pudo leer modrinth.index.json.");

            var result = new ModpackImportResult
            {
                ModpackName = string.IsNullOrEmpty(index.Name) ? Path.GetFileNameWithoutExtension(mrpackPath) : index.Name
            };

            if (index.Dependencies.TryGetValue("minecraft", out var mc))
                result.MinecraftVersion = mc;
            if (index.Dependencies.TryGetValue("neoforge", out var neo))
                result.RequiredLoader = $"NeoForge {neo}";
            else if (index.Dependencies.TryGetValue("forge", out var forge))
                result.RequiredLoader = $"Forge {forge}";
            else if (index.Dependencies.TryGetValue("fabric-loader", out var fabric))
                result.RequiredLoader = $"Fabric Loader {fabric}";
            else if (index.Dependencies.TryGetValue("quilt-loader", out var quilt))
                result.RequiredLoader = $"Quilt Loader {quilt}";

            _logService.Info("ModpackService", "Import", $"Instalando {index.Files.Count} archivos de '{result.ModpackName}'...");

            foreach (var file in index.Files)
            {
                if (file.Env?.Client == "unsupported") continue;
                if (string.IsNullOrEmpty(file.Path)) continue;
                if (file.Downloads.Count == 0) continue;

                var safePath = file.Path.Replace('\\', '/').TrimStart('/');
                var targetPath = Path.Combine(gameDir, safePath);
                if (!Path.GetFullPath(targetPath).StartsWith(Path.GetFullPath(gameDir), StringComparison.OrdinalIgnoreCase))
                {
                    result.Warnings.Add($"Archivo ignorado por ruta no válida: {file.Path}");
                    continue;
                }

                var fileName = Path.GetFileName(safePath);
                onProgress?.Invoke($"Descargando {fileName}...");

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    await DownloadWithHashCheckAsync(file.Downloads[0], targetPath, file.Hashes?.Sha1, file.FileSize);

                    if (safePath.StartsWith("mods/") && fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    {
                        var existing = await _modRepository.GetByProfileIdAsync(profileId);
                        var alreadyInstalled = existing.Any(m =>
                            string.Equals(m.FileName, fileName, StringComparison.OrdinalIgnoreCase));

                        if (!alreadyInstalled)
                        {
                            await _modRepository.CreateAsync(new Mod
                            {
                                ProfileId = profileId,
                                Name = Path.GetFileNameWithoutExtension(fileName),
                                FileName = fileName,
                                FilePath = targetPath,
                                FileSizeBytes = new FileInfo(targetPath).Length,
                                MinecraftVersion = result.MinecraftVersion ?? string.Empty,
                                Status = ModStatus.Active
                            });
                            result.ModsInstalled++;
                        }
                    }
                    result.FilesInstalled++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to install modpack file {File}", file.Path);
                    result.Warnings.Add($"No se pudo instalar {file.Path}: {ex.Message}");
                }
            }

            var overridesDir = Path.Combine(tempDir, "overrides");
            if (Directory.Exists(overridesDir))
            {
                onProgress?.Invoke("Aplicando overrides...");
                CopyDirectory(overridesDir, gameDir);
            }

            _logService.Info("ModpackService", "Import", $"Modpack '{result.ModpackName}' importado ({result.ModsInstalled} mods).");
            return result;
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private async Task DownloadWithHashCheckAsync(string url, string destPath, string? expectedSha1, long expectedSize)
    {
        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ShoroCraftLauncher/1.0");

        using var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Descarga fallida ({(int)response.StatusCode}).");

        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        await input.CopyToAsync(output);
        await output.FlushAsync();

        if (expectedSize > 0 && new FileInfo(destPath).Length != expectedSize)
            throw new Exception("Tamaño del archivo no coincide.");

        if (!string.IsNullOrEmpty(expectedSha1))
        {
            await using var fileStream = File.OpenRead(destPath);
            var hash = Convert.ToHexString(await SHA1.HashDataAsync(fileStream)).ToLowerInvariant();
            if (!string.Equals(hash, expectedSha1.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
                throw new Exception("Hash SHA1 no coincide.");
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, dir);
            Directory.CreateDirectory(Path.Combine(dest, rel));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }
}
