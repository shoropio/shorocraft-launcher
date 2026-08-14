using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Core.Enums;

namespace ShoroCraftLauncher.Infrastructure.Services;

public class ProfileService : IProfileService
{
    private readonly IProfileRepository _profileRepo;
    private readonly IModRepository _modRepo;
    private readonly IShaderPackRepository _shaderPackRepo;
    private readonly IResourcePackRepository _resourcePackRepo;
    private readonly IGameMapRepository _gameMapRepo;
    private readonly IScriptRepository _scriptRepo;
    private readonly IModService _modService;
    private readonly IMinecraftService _minecraftService;
    private readonly ILogService _logService;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private Profile? _selectedProfile;

    public Profile? SelectedProfile
    {
        get => _selectedProfile;
        set => SetSelectedProfile(value);
    }

    public ObservableCollection<Profile> Profiles { get; } = new();

    public event Action? SelectedProfileChanged;

    public ProfileService(
        IProfileRepository profileRepo,
        IModRepository modRepo,
        IShaderPackRepository shaderPackRepo,
        IResourcePackRepository resourcePackRepo,
        IGameMapRepository gameMapRepo,
        IScriptRepository scriptRepo,
        IModService modService,
        IMinecraftService minecraftService,
        ILogService logService)
    {
        _profileRepo = profileRepo;
        _modRepo = modRepo;
        _shaderPackRepo = shaderPackRepo;
        _resourcePackRepo = resourcePackRepo;
        _gameMapRepo = gameMapRepo;
        _scriptRepo = scriptRepo;
        _modService = modService;
        _minecraftService = minecraftService;
        _logService = logService;
    }

    public async Task LoadProfilesAsync()
    {
        await _loadLock.WaitAsync();
        try
        {
            var selectedId = SelectedProfile?.Id;
            var profiles = await _profileRepo.GetAllAsync();
            Profiles.Clear();
            foreach (var p in profiles)
            {
                Profiles.Add(p);
            }

            if (Profiles.Count == 0)
            {
                var defaultProfile = new Profile
                {
                    Name = "Vanilla",
                    MinecraftVersion = "latest",
                    Type = ShoroCraftLauncher.Core.Enums.ProfileType.Vanilla,
                    MinRamMB = 2048,
                    MaxRamMB = 4096,
                    WindowWidth = 854,
                    WindowHeight = 480
                };
                await _profileRepo.CreateAsync(defaultProfile);
                Profiles.Add(defaultProfile);
            }

            if (selectedId is null && Profiles.Count > 0)
            {
                SelectedProfile = Profiles[0];
            }
            else if (selectedId is not null)
            {
                var existing = Profiles.FirstOrDefault(p => p.Id == selectedId.Value);
                if (existing != null) SelectedProfile = existing;
                else SelectedProfile = Profiles.FirstOrDefault();
            }
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public async Task UpdateProfileAsync(Profile profile)
    {
        await _profileRepo.UpdateAsync(profile);

        var idx = Profiles.ToList().FindIndex(p => p.Id == profile.Id);
        if (idx >= 0)
        {
            if (!ReferenceEquals(Profiles[idx], profile))
                Profiles[idx] = profile;
        }

        if (SelectedProfile?.Id == profile.Id || Profiles.Count == 1)
            SetSelectedProfile(idx >= 0 ? Profiles[idx] : profile, forceNotify: true);
    }

    private void SetSelectedProfile(Profile? profile, bool forceNotify = false)
    {
        if (!forceNotify && ReferenceEquals(_selectedProfile, profile))
            return;

        _selectedProfile = profile;
        SelectedProfileChanged?.Invoke();
    }

    public async Task SyncProfileFilesAsync(Profile profile)
    {
        if (profile == null) return;
        using var operation = _logService?.BeginOperation("ProfileSync", "SyncProfileFiles", new { profile.Name });

        var gameDir = string.IsNullOrEmpty(profile.GameDirectory)
            ? _minecraftService.GetDefaultGameDirectory(profile.Name)
            : profile.GameDirectory;

        if (!Directory.Exists(gameDir))
        {
            Directory.CreateDirectory(gameDir);
            _logService?.Info("ProfileSync", "DirectoryRecreated", "La carpeta del perfil no existía; la recreé.", new { gameDir });
        }

        var modsDir = _minecraftService.GetModsDirectory(gameDir);
        var shadersDir = Path.Combine(gameDir, "shaderpacks");
        var resourcepacksDir = Path.Combine(gameDir, "resourcepacks");
        var savesDir = _minecraftService.GetSavesDirectory(gameDir);
        var scriptsDir = Path.Combine(gameDir, "scripts", _minecraftService.SanitizeProfileFolderName(profile.Name));

        Directory.CreateDirectory(modsDir);
        Directory.CreateDirectory(shadersDir);
        Directory.CreateDirectory(resourcepacksDir);
        Directory.CreateDirectory(savesDir);
        Directory.CreateDirectory(scriptsDir);

        // 1. Sync mods
        try
        {
            var jarFiles = Directory.GetFiles(modsDir, "*.jar")
                .Concat(Directory.GetFiles(modsDir, "*.jar.disabled"))
                .ToArray();
            var dbMods = await _modRepo.GetByProfileIdAsync(profile.Id);

            var onDisk = new Dictionary<string, (string FilePath, ModStatus Status)>(StringComparer.OrdinalIgnoreCase);
            foreach (var jar in jarFiles)
            {
                var fileName = Path.GetFileName(jar);
                var logicalName = fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                    ? fileName[..^".disabled".Length]
                    : fileName;
                var disabled = !string.Equals(fileName, logicalName, StringComparison.OrdinalIgnoreCase);
                onDisk[logicalName] = (jar, disabled ? ModStatus.Inactive : ModStatus.Active);
            }

            // Add missing to DB / align status and path with disk
            foreach (var (logicalName, entry) in onDisk)
            {
                var existing = dbMods.FirstOrDefault(m =>
                    string.Equals(m.FileName, logicalName, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    var modInfo = await _modService.ExtractModInfoAsync(entry.FilePath);
                    var mod = new Mod
                    {
                        ProfileId = profile.Id,
                        Name = modInfo.Name ?? Path.GetFileNameWithoutExtension(logicalName),
                        FileName = logicalName,
                        FilePath = entry.FilePath,
                        FileSizeBytes = new FileInfo(entry.FilePath).Length,
                        MinecraftVersion = modInfo.MinecraftVersion ?? profile.MinecraftVersion,
                        ModVersion = modInfo.ModVersion ?? "unknown",
                        Status = entry.Status
                    };
                    await _modRepo.CreateAsync(mod);
                }
                else if (!string.Equals(existing.FilePath, entry.FilePath, StringComparison.OrdinalIgnoreCase)
                         || existing.Status != entry.Status)
                {
                    existing.FilePath = entry.FilePath;
                    existing.Status = entry.Status;
                    await _modRepo.UpdateAsync(existing);
                }
            }

            // Remove from DB only if the logical file is not on disk
            foreach (var mod in dbMods)
            {
                if (!onDisk.ContainsKey(mod.FileName))
                {
                    await _modRepo.DeleteAsync(mod.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logService?.Error("ProfileSync", "ModsSyncFailed", "Error sincronizando mods.", ex);
        }

        // 2. Sync shaders
        try
        {
            var zipFiles = Directory.GetFiles(shadersDir, "*.zip");
            var zipFileNames = zipFiles.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var dbShaders = await _shaderPackRepo.GetByProfileIdAsync(profile.Id);

            foreach (var zip in zipFiles)
            {
                var fileName = Path.GetFileName(zip);
                if (!dbShaders.Any(s => string.Equals(s.FileName, fileName, StringComparison.OrdinalIgnoreCase)))
                {
                    var shader = new ShaderPack
                    {
                        ProfileId = profile.Id,
                        Name = Path.GetFileNameWithoutExtension(fileName),
                        FileName = fileName,
                        FilePath = zip,
                        FileSizeBytes = new FileInfo(zip).Length,
                        Status = PackStatus.Active
                    };
                    await _shaderPackRepo.CreateAsync(shader);
                }
            }

            foreach (var shader in dbShaders)
            {
                if (!zipFileNames.Contains(shader.FileName))
                {
                    await _shaderPackRepo.DeleteAsync(shader.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logService?.Error("ProfileSync", "ShadersSyncFailed", "Error sincronizando shaders.", ex);
        }

        // 3. Sync resourcepacks
        try
        {
            var zipFiles = Directory.GetFiles(resourcepacksDir, "*.zip");
            var zipFileNames = zipFiles.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var dbPacks = await _resourcePackRepo.GetByProfileIdAsync(profile.Id);

            foreach (var zip in zipFiles)
            {
                var fileName = Path.GetFileName(zip);
                if (!dbPacks.Any(p => string.Equals(p.FileName, fileName, StringComparison.OrdinalIgnoreCase)))
                {
                    var previewPath = string.Empty;
                    try
                    {
                        using var archive = ZipFile.OpenRead(zip);
                        var entry = archive.GetEntry("pack.png");
                        if (entry != null)
                        {
                            var previewDir = Path.Combine(resourcepacksDir, ".previews");
                            Directory.CreateDirectory(previewDir);
                            previewPath = Path.Combine(previewDir, $"{Path.GetFileNameWithoutExtension(fileName)}.png");
                            entry.ExtractToFile(previewPath, true);
                        }
                    }
                    catch { }

                    var pack = new ResourcePack
                    {
                        ProfileId = profile.Id,
                        Name = Path.GetFileNameWithoutExtension(fileName),
                        FileName = fileName,
                        FilePath = zip,
                        FileSizeBytes = new FileInfo(zip).Length,
                        PreviewImagePath = previewPath,
                        Status = PackStatus.Active
                    };
                    await _resourcePackRepo.CreateAsync(pack);
                }
            }

            foreach (var pack in dbPacks)
            {
                if (!zipFileNames.Contains(pack.FileName))
                {
                    await _resourcePackRepo.DeleteAsync(pack.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logService?.Error("ProfileSync", "ResourcePacksSyncFailed", "Error sincronizando resourcepacks.", ex);
        }

        // 4. Sync saves (worlds)
        try
        {
            var files = Directory.GetFiles(savesDir, "*.*")
                .Where(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".mcworld", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var dirs = Directory.GetDirectories(savesDir)
                .Where(d => !Path.GetFileName(d).Equals(".previews", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var diskMapNames = files.Select(Path.GetFileName)
                .Concat(dirs.Select(Path.GetFileName))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var dbMaps = await _gameMapRepo.GetByProfileIdAsync(profile.Id);

            // Add missing files/folders
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                if (!dbMaps.Any(m => string.Equals(m.FileName, fileName, StringComparison.OrdinalIgnoreCase)))
                {
                    var previewPath = string.Empty;
                    try
                    {
                        using var archive = ZipFile.OpenRead(file);
                        var entry = archive.GetEntry("worldicon.png") ?? archive.GetEntry("icon.png");
                        if (entry != null)
                        {
                            var previewDir = Path.Combine(savesDir, ".previews");
                            Directory.CreateDirectory(previewDir);
                            previewPath = Path.Combine(previewDir, $"{Path.GetFileNameWithoutExtension(fileName)}.png");
                            entry.ExtractToFile(previewPath, true);
                        }
                    }
                    catch { }

                    var map = new GameMap
                    {
                        ProfileId = profile.Id,
                        Name = Path.GetFileNameWithoutExtension(fileName),
                        FileName = fileName,
                        FilePath = file,
                        FileSizeBytes = new FileInfo(file).Length,
                        PreviewImagePath = previewPath,
                        Status = PackStatus.Active
                    };
                    await _gameMapRepo.CreateAsync(map);
                }
            }

            foreach (var dir in dirs)
            {
                var dirName = Path.GetFileName(dir);
                if (!dbMaps.Any(m => string.Equals(m.FileName, dirName, StringComparison.OrdinalIgnoreCase)))
                {
                    var previewPath = Path.Combine(dir, "icon.png");
                    if (!File.Exists(previewPath)) previewPath = Path.Combine(dir, "worldicon.png");
                    if (!File.Exists(previewPath)) previewPath = string.Empty;

                    long size = 0;
                    try
                    {
                        size = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                            .Sum(f => new FileInfo(f).Length);
                    }
                    catch { }

                    var map = new GameMap
                    {
                        ProfileId = profile.Id,
                        Name = dirName,
                        FileName = dirName,
                        FilePath = dir,
                        FileSizeBytes = size,
                        PreviewImagePath = previewPath,
                        Status = PackStatus.Active
                    };
                    await _gameMapRepo.CreateAsync(map);
                }
            }

            // Remove missing from DB
            foreach (var map in dbMaps)
            {
                if (!diskMapNames.Contains(map.FileName))
                {
                    await _gameMapRepo.DeleteAsync(map.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logService?.Error("ProfileSync", "GameMapsSyncFailed", "Error sincronizando mundos.", ex);
        }

        // 5. Sync scripts
        try
        {
            var scriptFiles = Directory.GetFiles(scriptsDir, "*.*")
                .Where(f => f.EndsWith(".js", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var scriptFileNames = scriptFiles.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var dbScripts = await _scriptRepo.GetByProfileIdAsync(profile.Id);

            foreach (var file in scriptFiles)
            {
                var fileName = Path.GetFileName(file);
                if (!dbScripts.Any(s => string.Equals(s.FileName, fileName, StringComparison.OrdinalIgnoreCase)))
                {
                    var script = new Script
                    {
                        ProfileId = profile.Id,
                        Name = Path.GetFileNameWithoutExtension(fileName),
                        FileName = fileName,
                        FilePath = file,
                        CreatedAt = DateTime.UtcNow,
                        ModifiedAt = DateTime.UtcNow
                    };
                    await _scriptRepo.CreateAsync(script);
                }
            }

            foreach (var script in dbScripts)
            {
                if (!scriptFileNames.Contains(script.FileName))
                {
                    await _scriptRepo.DeleteAsync(script.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logService?.Error("ProfileSync", "ScriptsSyncFailed", "Error sincronizando scripts.", ex);
        }
    }

    public async Task ExportProfileAsync(int profileId, string exportZipPath)
    {
        var profile = await _profileRepo.GetByIdAsync(profileId)
            ?? throw new Exception($"Profile {profileId} not found");

        var gameDir = string.IsNullOrEmpty(profile.GameDirectory)
            ? _minecraftService.GetDefaultGameDirectory(profile.Name)
            : profile.GameDirectory;

        var tempRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShoroCraftLauncher", "temp_export_" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(tempRoot);

        try
        {
            var exportProfile = new Profile
            {
                Name = profile.Name,
                MinecraftVersion = profile.MinecraftVersion,
                Type = profile.Type,
                MinRamMB = profile.MinRamMB,
                MaxRamMB = profile.MaxRamMB,
                WindowWidth = profile.WindowWidth,
                WindowHeight = profile.WindowHeight,
                JvmArguments = profile.JvmArguments,
                LoaderVersion = profile.LoaderVersion,
                IsFullscreen = profile.IsFullscreen,
                JavaPath = string.Empty,
                GameDirectory = string.Empty
            };

            var profileJson = JsonSerializer.Serialize(exportProfile, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(tempRoot, "profile.json"), profileJson);

            void CopyDirectory(string source, string dest)
            {
                if (!Directory.Exists(source)) return;
                Directory.CreateDirectory(dest);
                foreach (var file in Directory.GetFiles(source))
                {
                    File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
                }
                foreach (var sub in Directory.GetDirectories(source))
                {
                    var subName = Path.GetFileName(sub);
                    CopyDirectory(sub, Path.Combine(dest, subName));
                }
            }

            var folders = new[] { "mods", "shaderpacks", "resourcepacks", "saves" };
            foreach (var folder in folders)
            {
                var src = Path.Combine(gameDir, folder);
                if (Directory.Exists(src))
                {
                    CopyDirectory(src, Path.Combine(tempRoot, folder));
                }
            }

            var scriptsSrc = Path.Combine(gameDir, "scripts", profile.Name);
            if (Directory.Exists(scriptsSrc))
            {
                CopyDirectory(scriptsSrc, Path.Combine(tempRoot, "scripts"));
            }

            if (File.Exists(exportZipPath)) File.Delete(exportZipPath);
            ZipFile.CreateFromDirectory(tempRoot, exportZipPath);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
            }
            catch { }
        }
    }

    public async Task ImportProfileAsync(string importZipPath)
    {
        if (!File.Exists(importZipPath))
            throw new FileNotFoundException("El archivo de paquete no existe.", importZipPath);

        var tempRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShoroCraftLauncher", "temp_import_" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(tempRoot);

        try
        {
            ZipFile.ExtractToDirectory(importZipPath, tempRoot);

            var jsonPath = Path.Combine(tempRoot, "profile.json");
            if (!File.Exists(jsonPath))
                throw new Exception("El archivo no es un paquete válido de ShoroCraft (falta profile.json).");

            var jsonContent = await File.ReadAllTextAsync(jsonPath);
            var importedProfile = JsonSerializer.Deserialize<Profile>(jsonContent)
                ?? throw new Exception("Error al deserializar la información del perfil.");

            var originalName = importedProfile.Name;
            var name = originalName;
            var counter = 1;
            var existingProfiles = await _profileRepo.GetAllAsync();
            while (existingProfiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                name = $"{originalName} ({counter++})";
            }

            var profileToImport = new Profile
            {
                Name = name,
                MinecraftVersion = importedProfile.MinecraftVersion,
                Type = importedProfile.Type,
                MinRamMB = importedProfile.MinRamMB,
                MaxRamMB = importedProfile.MaxRamMB,
                WindowWidth = importedProfile.WindowWidth,
                WindowHeight = importedProfile.WindowHeight,
                JvmArguments = importedProfile.JvmArguments,
                LoaderVersion = importedProfile.LoaderVersion,
                IsFullscreen = importedProfile.IsFullscreen,
                JavaPath = string.Empty,
                GameDirectory = string.Empty
            };

            var newGameDir = _minecraftService.GetDefaultGameDirectory(name);
            profileToImport.GameDirectory = newGameDir;

            Directory.CreateDirectory(newGameDir);

            void CopyDirectory(string source, string dest)
            {
                Directory.CreateDirectory(dest);
                foreach (var file in Directory.GetFiles(source))
                {
                    File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
                }
                foreach (var sub in Directory.GetDirectories(source))
                {
                    CopyDirectory(sub, Path.Combine(dest, Path.GetFileName(sub)));
                }
            }

            var folders = new[] { "mods", "shaderpacks", "resourcepacks", "saves" };
            foreach (var folder in folders)
            {
                var src = Path.Combine(tempRoot, folder);
                if (Directory.Exists(src))
                {
                    CopyDirectory(src, Path.Combine(newGameDir, folder));
                }
            }

            var scriptsSrc = Path.Combine(tempRoot, "scripts");
            if (Directory.Exists(scriptsSrc))
            {
                CopyDirectory(scriptsSrc, Path.Combine(newGameDir, "scripts", name));
            }

            await _profileRepo.CreateAsync(profileToImport);
            await SyncProfileFilesAsync(profileToImport);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
            }
            catch { }
        }
    }

    public async Task CreateBackupAsync(int profileId, string backupType)
    {
        var profile = await _profileRepo.GetByIdAsync(profileId)
            ?? throw new Exception($"Profile {profileId} not found");

        var gameDir = string.IsNullOrEmpty(profile.GameDirectory)
            ? _minecraftService.GetDefaultGameDirectory(profile.Name)
            : profile.GameDirectory;

        var backupsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShoroCraftLauncher", "backups", profile.Name);

        Directory.CreateDirectory(backupsDir);

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var zipPath = Path.Combine(backupsDir, $"{backupType}_{timestamp}.zip");

        var tempDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShoroCraftLauncher", "temp_backup_" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(tempDir);

        try
        {
            void CopyDirectory(string source, string dest)
            {
                if (!Directory.Exists(source)) return;
                Directory.CreateDirectory(dest);
                foreach (var file in Directory.GetFiles(source))
                {
                    File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
                }
                foreach (var sub in Directory.GetDirectories(source))
                {
                    var subName = Path.GetFileName(sub);
                    if (subName.Equals("backups", StringComparison.OrdinalIgnoreCase)) continue;
                    CopyDirectory(sub, Path.Combine(dest, subName));
                }
            }

            if (backupType.Equals("All", StringComparison.OrdinalIgnoreCase) || backupType.Equals("Worlds", StringComparison.OrdinalIgnoreCase))
            {
                var savesSrc = _minecraftService.GetSavesDirectory(gameDir);
                if (Directory.Exists(savesSrc))
                {
                    CopyDirectory(savesSrc, Path.Combine(tempDir, "saves"));
                }
            }

            if (backupType.Equals("All", StringComparison.OrdinalIgnoreCase) || backupType.Equals("Scripts", StringComparison.OrdinalIgnoreCase))
            {
                var scriptsSrc = Path.Combine(gameDir, "scripts", profile.Name);
                if (Directory.Exists(scriptsSrc))
                {
                    CopyDirectory(scriptsSrc, Path.Combine(tempDir, "scripts"));
                }
            }

            if (backupType.Equals("All", StringComparison.OrdinalIgnoreCase) || backupType.Equals("Configs", StringComparison.OrdinalIgnoreCase))
            {
                var filesToCopy = new[] { "options.txt", "optionsof.txt", "optionsshaders.txt" };
                foreach (var file in filesToCopy)
                {
                    var path = Path.Combine(gameDir, file);
                    if (File.Exists(path))
                    {
                        File.Copy(path, Path.Combine(tempDir, file), true);
                    }
                }

                var configSrc = Path.Combine(gameDir, "config");
                if (Directory.Exists(configSrc))
                {
                    CopyDirectory(configSrc, Path.Combine(tempDir, "config"));
                }
            }

            ZipFile.CreateFromDirectory(tempDir, zipPath);
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    public async Task RestoreBackupAsync(int profileId, string backupZipPath)
    {
        if (!File.Exists(backupZipPath))
            throw new FileNotFoundException("El archivo de copia de seguridad no existe.", backupZipPath);

        var profile = await _profileRepo.GetByIdAsync(profileId)
            ?? throw new Exception($"Profile {profileId} not found");

        var gameDir = string.IsNullOrEmpty(profile.GameDirectory)
            ? _minecraftService.GetDefaultGameDirectory(profile.Name)
            : profile.GameDirectory;

        var tempDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShoroCraftLauncher", "temp_restore_" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(tempDir);

        try
        {
            ZipFile.ExtractToDirectory(backupZipPath, tempDir);

            void CopyDirectory(string source, string dest)
            {
                Directory.CreateDirectory(dest);
                foreach (var file in Directory.GetFiles(source))
                {
                    File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
                }
                foreach (var sub in Directory.GetDirectories(source))
                {
                    CopyDirectory(sub, Path.Combine(dest, Path.GetFileName(sub)));
                }
            }

            // Restore saves
            var savesSrc = Path.Combine(tempDir, "saves");
            if (Directory.Exists(savesSrc))
            {
                var dest = _minecraftService.GetSavesDirectory(gameDir);
                try { if (Directory.Exists(dest)) Directory.Delete(dest, true); } catch { }
                CopyDirectory(savesSrc, dest);
            }

            // Restore scripts
            var scriptsSrc = Path.Combine(tempDir, "scripts");
            if (Directory.Exists(scriptsSrc))
            {
                var dest = Path.Combine(gameDir, "scripts", profile.Name);
                try { if (Directory.Exists(dest)) Directory.Delete(dest, true); } catch { }
                CopyDirectory(scriptsSrc, dest);
            }

            // Restore configs
            var configSrc = Path.Combine(tempDir, "config");
            if (Directory.Exists(configSrc))
            {
                var dest = Path.Combine(gameDir, "config");
                try { if (Directory.Exists(dest)) Directory.Delete(dest, true); } catch { }
                CopyDirectory(configSrc, dest);
            }

            var configFiles = new[] { "options.txt", "optionsof.txt", "optionsshaders.txt" };
            foreach (var file in configFiles)
            {
                var srcPath = Path.Combine(tempDir, file);
                if (File.Exists(srcPath))
                {
                    File.Copy(srcPath, Path.Combine(gameDir, file), true);
                }
            }

            await SyncProfileFilesAsync(profile);
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    public Task DeleteBackupAsync(int profileId, string backupZipPath)
    {
        if (File.Exists(backupZipPath))
        {
            File.Delete(backupZipPath);
        }
        return Task.CompletedTask;
    }

    public async Task<List<BackupItem>> GetBackupsAsync(int profileId)
    {
        var profile = await _profileRepo.GetByIdAsync(profileId);
        if (profile == null) return new List<BackupItem>();

        var backupsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShoroCraftLauncher", "backups", profile.Name);

        if (!Directory.Exists(backupsDir))
            return new List<BackupItem>();

        var list = new List<BackupItem>();
        foreach (var file in Directory.GetFiles(backupsDir, "*.zip"))
        {
            var fileName = Path.GetFileName(file);
            var parts = Path.GetFileNameWithoutExtension(fileName).Split('_');
            if (parts.Length >= 3)
            {
                var type = parts[0];
                var dateStr = parts[1];
                var timeStr = parts[2];
                if (DateTime.TryParseExact($"{dateStr}_{timeStr}", "yyyyMMdd_HHmmss", null, System.Globalization.DateTimeStyles.None, out var timestamp))
                {
                    list.Add(new BackupItem
                    {
                        FilePath = file,
                        FileName = fileName,
                        BackupType = type,
                        Timestamp = timestamp,
                        FileSizeBytes = new FileInfo(file).Length
                    });
                }
            }
        }

        return list.OrderByDescending(b => b.Timestamp).ToList();
    }
}
