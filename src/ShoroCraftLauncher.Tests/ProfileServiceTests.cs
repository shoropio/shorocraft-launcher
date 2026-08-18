using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;
using Moq;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Infrastructure.Services;
using Xunit;

namespace ShoroCraftLauncher.Tests;

public class ProfileServiceTests
{
    private readonly ILogger<ProfileService> _logger = Mock.Of<ILogger<ProfileService>>();
    private readonly Mock<ILogService> _logService = new(MockBehavior.Loose);

    private ProfileService CreateProfileService(
        Mock<IProfileRepository>? profileRepository = null,
        Mock<IModRepository>? modRepository = null,
        Mock<IShaderPackRepository>? shaderPackRepository = null,
        Mock<IResourcePackRepository>? resourcePackRepository = null,
        Mock<IGameMapRepository>? gameMapRepository = null,
        Mock<IScriptRepository>? scriptRepository = null,
        Mock<IModService>? modService = null,
        Mock<IMinecraftService>? minecraftService = null)
    {
        return new ProfileService(
            profileRepository?.Object ?? Mock.Of<IProfileRepository>(),
            modRepository?.Object ?? Mock.Of<IModRepository>(),
            shaderPackRepository?.Object ?? Mock.Of<IShaderPackRepository>(),
            resourcePackRepository?.Object ?? Mock.Of<IResourcePackRepository>(),
            gameMapRepository?.Object ?? Mock.Of<IGameMapRepository>(),
            scriptRepository?.Object ?? Mock.Of<IScriptRepository>(),
            modService?.Object ?? Mock.Of<IModService>(),
            minecraftService?.Object ?? Mock.Of<IMinecraftService>(),
            _logService.Object);
    }

    [Fact]
    public async Task SyncProfileFilesAsync_AddsMissingAssetsAndRemovesDeletedEntries()
    {
        var tempDir = TestPaths.CreateTempDir("ShoroCraftProfileSync");
        try
        {
            var profile = new Profile
            {
                Id = 1,
                Name = "TestProfile",
                MinecraftVersion = "1.21.4",
                Type = ProfileType.Vanilla,
                MinRamMB = 2048,
                MaxRamMB = 4096,
                WindowWidth = 854,
                WindowHeight = 480,
                GameDirectory = tempDir
            };

            var modsDir = Path.Combine(tempDir, "mods");
            var shadersDir = Path.Combine(tempDir, "shaderpacks");
            var resourcePacksDir = Path.Combine(tempDir, "resourcepacks");
            var savesDir = Path.Combine(tempDir, "saves");
            var scriptsDir = Path.Combine(tempDir, "scripts", profile.Name);

            Directory.CreateDirectory(modsDir);
            Directory.CreateDirectory(shadersDir);
            Directory.CreateDirectory(resourcePacksDir);
            Directory.CreateDirectory(savesDir);
            Directory.CreateDirectory(scriptsDir);

            var testModPath = Path.Combine(modsDir, "new-mod.jar");
            await File.WriteAllTextAsync(testModPath, "mod-content");
            await File.WriteAllTextAsync(Path.Combine(shadersDir, "shaderpack.zip"), "shader-content");
            await File.WriteAllTextAsync(Path.Combine(resourcePacksDir, "resourcepack.zip"), "resource-content");
            var worldDir = Path.Combine(savesDir, "ExampleWorld");
            Directory.CreateDirectory(worldDir);
            await File.WriteAllTextAsync(Path.Combine(scriptsDir, "test-script.js"), "console.log('hello');");

            var dbMods = new List<Mod>
            {
                new() { Id = 5, ProfileId = profile.Id, FileName = "old-mod.jar", FilePath = Path.Combine(modsDir, "old-mod.jar"), Status = ModStatus.Active }
            };
            var dbShaders = new List<ShaderPack>();
            var dbResources = new List<ResourcePack>();
            var dbMaps = new List<GameMap>();
            var dbScripts = new List<Script>();

            var mockProfileRepo = new Mock<IProfileRepository>(MockBehavior.Strict);
            mockProfileRepo.Setup(x => x.GetByIdAsync(profile.Id)).ReturnsAsync(profile);

            var mockModRepo = new Mock<IModRepository>(MockBehavior.Strict);
            mockModRepo.Setup(x => x.GetByProfileIdAsync(profile.Id)).ReturnsAsync(dbMods);
            mockModRepo.Setup(x => x.CreateAsync(It.IsAny<Mod>())).ReturnsAsync((Mod mod) =>
            {
                mod.Id = 10;
                dbMods.Add(mod);
                return mod.Id;
            });
            mockModRepo.Setup(x => x.DeleteAsync(It.IsAny<int>())).Returns<int>(id =>
            {
                dbMods.RemoveAll(m => m.Id == id);
                return Task.CompletedTask;
            });

            var mockShaderRepo = new Mock<IShaderPackRepository>(MockBehavior.Strict);
            mockShaderRepo.Setup(x => x.GetByProfileIdAsync(profile.Id)).ReturnsAsync(dbShaders);
            mockShaderRepo.Setup(x => x.CreateAsync(It.IsAny<ShaderPack>())).ReturnsAsync((ShaderPack shader) =>
            {
                shader.Id = 20;
                dbShaders.Add(shader);
                return shader.Id;
            });
            mockShaderRepo.Setup(x => x.DeleteAsync(It.IsAny<int>())).Returns<int>(id =>
            {
                dbShaders.RemoveAll(s => s.Id == id);
                return Task.CompletedTask;
            });

            var mockResourceRepo = new Mock<IResourcePackRepository>(MockBehavior.Strict);
            mockResourceRepo.Setup(x => x.GetByProfileIdAsync(profile.Id)).ReturnsAsync(dbResources);
            mockResourceRepo.Setup(x => x.CreateAsync(It.IsAny<ResourcePack>())).ReturnsAsync((ResourcePack pack) =>
            {
                pack.Id = 30;
                dbResources.Add(pack);
                return pack.Id;
            });
            mockResourceRepo.Setup(x => x.DeleteAsync(It.IsAny<int>())).Returns<int>(id =>
            {
                dbResources.RemoveAll(r => r.Id == id);
                return Task.CompletedTask;
            });

            var mockMapRepo = new Mock<IGameMapRepository>(MockBehavior.Strict);
            mockMapRepo.Setup(x => x.GetByProfileIdAsync(profile.Id)).ReturnsAsync(dbMaps);
            mockMapRepo.Setup(x => x.CreateAsync(It.IsAny<GameMap>())).ReturnsAsync((GameMap map) =>
            {
                map.Id = 40;
                dbMaps.Add(map);
                return map.Id;
            });
            mockMapRepo.Setup(x => x.DeleteAsync(It.IsAny<int>())).Returns<int>(id =>
            {
                dbMaps.RemoveAll(m => m.Id == id);
                return Task.CompletedTask;
            });

            var mockScriptRepo = new Mock<IScriptRepository>(MockBehavior.Strict);
            mockScriptRepo.Setup(x => x.GetByProfileIdAsync(profile.Id)).ReturnsAsync(dbScripts);
            mockScriptRepo.Setup(x => x.CreateAsync(It.IsAny<Script>())).ReturnsAsync((Script script) =>
            {
                script.Id = 50;
                dbScripts.Add(script);
                return script.Id;
            });
            mockScriptRepo.Setup(x => x.DeleteAsync(It.IsAny<int>())).Returns<int>(id =>
            {
                dbScripts.RemoveAll(s => s.Id == id);
                return Task.CompletedTask;
            });

            var mockModService = new Mock<IModService>(MockBehavior.Strict);
            mockModService.Setup(x => x.ExtractModInfoAsync(testModPath)).ReturnsAsync(("Example Mod", profile.MinecraftVersion, "1.0.0"));

            var mockMinecraft = new Mock<IMinecraftService>(MockBehavior.Strict);
            mockMinecraft.Setup(x => x.GetDefaultGameDirectory(profile.Name)).Returns(tempDir);
            mockMinecraft.Setup(x => x.GetModsDirectory(tempDir)).Returns(modsDir);
            mockMinecraft.Setup(x => x.GetSavesDirectory(tempDir)).Returns(savesDir);
            mockMinecraft.Setup(x => x.SanitizeProfileFolderName(It.IsAny<string>())).Returns((string name) => name);

            var service = CreateProfileService(
                mockProfileRepo,
                mockModRepo,
                mockShaderRepo,
                mockResourceRepo,
                mockMapRepo,
                mockScriptRepo,
                mockModService,
                mockMinecraft);

            await service.SyncProfileFilesAsync(profile);

            Assert.Contains(dbMods, m => m.FileName == "new-mod.jar");
            Assert.DoesNotContain(dbMods, m => m.FileName == "old-mod.jar");
            Assert.Contains(dbShaders, s => s.FileName == "shaderpack.zip");
            Assert.Contains(dbResources, r => r.FileName == "resourcepack.zip");
            Assert.Contains(dbMaps, m => m.FileName == "ExampleWorld");
            Assert.Contains(dbScripts, s => s.FileName == "test-script.js");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task ExportProfileAsync_CreatesPackageWithoutCustomPaths()
    {
        var tempDir = TestPaths.CreateTempDir("ShoroCraftProfileExport");
        try
        {
            using var dataRootScope = TestPaths.UseLauncherDataRoot("ShoroCraftProfileExportData", out _);
            var profile = new Profile
            {
                Id = 10,
                Name = "ExportProfile",
                MinecraftVersion = "1.21.4",
                Type = ProfileType.Vanilla,
                MinRamMB = 2048,
                MaxRamMB = 4096,
                WindowWidth = 854,
                WindowHeight = 480,
                JvmArguments = "-Xmx4G",
                LoaderVersion = "",
                IsFullscreen = false,
                JavaPath = "C:\\CustomJava\\bin\\javaw.exe",
                GameDirectory = tempDir
            };

            var modsDir = Path.Combine(tempDir, "mods");
            var scriptsDir = Path.Combine(tempDir, "scripts", profile.Name);
            var savesDir = Path.Combine(tempDir, "saves");
            Directory.CreateDirectory(modsDir);
            Directory.CreateDirectory(scriptsDir);
            Directory.CreateDirectory(savesDir);
            await File.WriteAllTextAsync(Path.Combine(modsDir, "example.jar"), "jar");
            await File.WriteAllTextAsync(Path.Combine(scriptsDir, "script.js"), "console.log('x');");
            var worldDir = Path.Combine(savesDir, "WorldOne");
            Directory.CreateDirectory(worldDir);
            await File.WriteAllTextAsync(Path.Combine(worldDir, "level.dat"), "level");

            var mockProfileRepo = new Mock<IProfileRepository>(MockBehavior.Strict);
            mockProfileRepo.Setup(x => x.GetByIdAsync(profile.Id)).ReturnsAsync(profile);

            var mockMinecraft = new Mock<IMinecraftService>(MockBehavior.Strict);
            mockMinecraft.Setup(x => x.GetDefaultGameDirectory(profile.Name)).Returns(tempDir);
            mockMinecraft.Setup(x => x.SanitizeProfileFolderName(It.IsAny<string>())).Returns((string name) => name);

            var service = CreateProfileService(profileRepository: mockProfileRepo, minecraftService: mockMinecraft);
            var exportPath = TestPaths.GetTempFile("ShoroCraftProfileExportZip", "shorocraft_export_test.zip");
            if (File.Exists(exportPath)) File.Delete(exportPath);

            await service.ExportProfileAsync(profile.Id, exportPath);

            Assert.True(File.Exists(exportPath));

            using var zip = ZipFile.OpenRead(exportPath);
            Assert.NotNull(zip.GetEntry("profile.json"));
            Assert.NotNull(zip.GetEntry("mods/example.jar"));
            Assert.NotNull(zip.GetEntry("scripts/script.js"));
            Assert.NotNull(zip.GetEntry("saves/WorldOne/level.dat"));

            using var profileStream = zip.GetEntry("profile.json")!.Open();
            using var reader = new StreamReader(profileStream, Encoding.UTF8);
            var json = await reader.ReadToEndAsync();
            Assert.Contains("\"GameDirectory\": \"\"", json);
            Assert.Contains("\"JavaPath\": \"\"", json);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task ImportProfileAsync_ResolvesNameCollisionAndSyncsFiles()
    {
        var tempDir = TestPaths.CreateTempDir("ShoroCraftProfileImport");
        try
        {
            using var dataRootScope = TestPaths.UseLauncherDataRoot("ShoroCraftProfileImportData", out _);
            Directory.CreateDirectory(tempDir);
            var existingProfile = new Profile
            {
                Id = 1,
                Name = "ImportedProfile",
                MinecraftVersion = "1.21.4",
                Type = ProfileType.Vanilla,
                MinRamMB = 2048,
                MaxRamMB = 4096,
                WindowWidth = 854,
                WindowHeight = 480,
                GameDirectory = Path.Combine(tempDir, "existing")
            };
            Directory.CreateDirectory(existingProfile.GameDirectory);

            var packageDir = Path.Combine(tempDir, "package");
            Directory.CreateDirectory(packageDir);
            var profileJson = new Profile
            {
                Name = "ImportedProfile",
                MinecraftVersion = "1.21.4",
                Type = ProfileType.Vanilla,
                MinRamMB = 1024,
                MaxRamMB = 2048,
                WindowWidth = 854,
                WindowHeight = 480,
                JvmArguments = "-Xmx2G",
                LoaderVersion = "",
                IsFullscreen = false,
                JavaPath = string.Empty,
                GameDirectory = string.Empty
            };
            await File.WriteAllTextAsync(Path.Combine(packageDir, "profile.json"), System.Text.Json.JsonSerializer.Serialize(profileJson));
            var scriptsDir = Path.Combine(packageDir, "scripts");
            Directory.CreateDirectory(scriptsDir);
            await File.WriteAllTextAsync(Path.Combine(scriptsDir, "import.js"), "console.log('import');");
            var zipPath = Path.Combine(tempDir, "import_package.zip");
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(packageDir, zipPath);

            var profiles = new List<Profile> { existingProfile };
            var nextProfileId = 1;
            var mockProfileRepo = new Mock<IProfileRepository>(MockBehavior.Strict);
            mockProfileRepo.Setup(x => x.GetAllAsync()).ReturnsAsync(profiles);
            mockProfileRepo.Setup(x => x.CreateAsync(It.IsAny<Profile>())).ReturnsAsync((Profile p) =>
            {
                p.Id = ++nextProfileId;
                profiles.Add(p);
                return p.Id;
            });
            mockProfileRepo.Setup(x => x.GetByIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => profiles.FirstOrDefault(p => p.Id == id));

            var scriptList = new List<Script>();
            var mockScriptRepo = new Mock<IScriptRepository>(MockBehavior.Strict);
            mockScriptRepo.Setup(x => x.GetByProfileIdAsync(It.IsAny<int>())).ReturnsAsync(scriptList);
            mockScriptRepo.Setup(x => x.CreateAsync(It.IsAny<Script>())).ReturnsAsync((Script s) =>
            {
                s.Id = 100;
                scriptList.Add(s);
                return s.Id;
            });
            mockScriptRepo.Setup(x => x.DeleteAsync(It.IsAny<int>())).Returns(Task.CompletedTask);

            var mockMinecraft = new Mock<IMinecraftService>(MockBehavior.Strict);
            mockMinecraft.Setup(x => x.GetDefaultGameDirectory(It.IsAny<string>())).Returns((string name) => Path.Combine(tempDir, "profiles", name));
            mockMinecraft.Setup(x => x.GetModsDirectory(It.IsAny<string>())).Returns((string gameDir) => Path.Combine(gameDir, "mods"));
            mockMinecraft.Setup(x => x.GetSavesDirectory(It.IsAny<string>())).Returns((string gameDir) => Path.Combine(gameDir, "saves"));
            mockMinecraft.Setup(x => x.SanitizeProfileFolderName(It.IsAny<string>())).Returns((string name) => name);

            var mockModRepo = new Mock<IModRepository>(MockBehavior.Strict);
            mockModRepo.Setup(x => x.GetByProfileIdAsync(It.IsAny<int>())).ReturnsAsync(new List<Mod>());
            mockModRepo.Setup(x => x.CreateAsync(It.IsAny<Mod>())).ReturnsAsync((Mod mod) => { mod.Id = 1; return mod.Id; });
            mockModRepo.Setup(x => x.DeleteAsync(It.IsAny<int>())).Returns(Task.CompletedTask);

            var mockShaderRepo = new Mock<IShaderPackRepository>(MockBehavior.Strict);
            mockShaderRepo.Setup(x => x.GetByProfileIdAsync(It.IsAny<int>())).ReturnsAsync(new List<ShaderPack>());
            mockShaderRepo.Setup(x => x.CreateAsync(It.IsAny<ShaderPack>())).ReturnsAsync((ShaderPack pack) => { pack.Id = 1; return pack.Id; });
            mockShaderRepo.Setup(x => x.DeleteAsync(It.IsAny<int>())).Returns(Task.CompletedTask);

            var mockResourceRepo = new Mock<IResourcePackRepository>(MockBehavior.Strict);
            mockResourceRepo.Setup(x => x.GetByProfileIdAsync(It.IsAny<int>())).ReturnsAsync(new List<ResourcePack>());
            mockResourceRepo.Setup(x => x.CreateAsync(It.IsAny<ResourcePack>())).ReturnsAsync((ResourcePack pack) => { pack.Id = 1; return pack.Id; });
            mockResourceRepo.Setup(x => x.DeleteAsync(It.IsAny<int>())).Returns(Task.CompletedTask);

            var service = CreateProfileService(
                profileRepository: mockProfileRepo,
                modRepository: mockModRepo,
                shaderPackRepository: mockShaderRepo,
                resourcePackRepository: mockResourceRepo,
                scriptRepository: mockScriptRepo,
                minecraftService: mockMinecraft);

            await service.ImportProfileAsync(zipPath);

            Assert.Equal(2, profiles.Count);
            var imported = profiles.FirstOrDefault(p => p.Id != existingProfile.Id);
            Assert.NotNull(imported);
            Assert.Equal("ImportedProfile (1)", imported!.Name);
            Assert.True(Directory.Exists(imported.GameDirectory));
            Assert.Single(scriptList);
            Assert.Contains(scriptList, s => s.FileName == "import.js");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task BackupMethods_CreateRestoreAndListBackups()
    {
        var tempDir = TestPaths.CreateTempDir("ShoroCraftProfileBackup");
        try
        {
            var profile = new Profile
            {
                Id = 9,
                Name = "BackupProfile",
                MinecraftVersion = "1.21.4",
                Type = ProfileType.Vanilla,
                MinRamMB = 2048,
                MaxRamMB = 4096,
                WindowWidth = 854,
                WindowHeight = 480,
                GameDirectory = tempDir
            };

            Directory.CreateDirectory(tempDir);
            var savesDir = Path.Combine(tempDir, "saves");
            Directory.CreateDirectory(Path.Combine(savesDir, "SavedWorld"));
            await File.WriteAllTextAsync(Path.Combine(savesDir, "SavedWorld", "level.dat"), "data");

            var mockProfileRepo = new Mock<IProfileRepository>(MockBehavior.Strict);
            mockProfileRepo.Setup(x => x.GetByIdAsync(profile.Id)).ReturnsAsync(profile);

            var mockMinecraft = new Mock<IMinecraftService>(MockBehavior.Strict);
            mockMinecraft.Setup(x => x.GetDefaultGameDirectory(It.IsAny<string>())).Returns(tempDir);
            mockMinecraft.Setup(x => x.GetModsDirectory(It.IsAny<string>())).Returns((string gameDir) => Path.Combine(gameDir, "mods"));
            mockMinecraft.Setup(x => x.GetSavesDirectory(It.IsAny<string>())).Returns((string gameDir) => Path.Combine(gameDir, "saves"));
            mockMinecraft.Setup(x => x.SanitizeProfileFolderName(It.IsAny<string>())).Returns((string name) => name);

            var mockModRepo = new Mock<IModRepository>(MockBehavior.Strict);
            mockModRepo.Setup(x => x.GetByProfileIdAsync(It.IsAny<int>())).ReturnsAsync(new List<Mod>());
            mockModRepo.Setup(x => x.CreateAsync(It.IsAny<Mod>())).ReturnsAsync((Mod mod) => { mod.Id = 1; return mod.Id; });
            mockModRepo.Setup(x => x.DeleteAsync(It.IsAny<int>())).Returns(Task.CompletedTask);

            var mockShaderRepo = new Mock<IShaderPackRepository>(MockBehavior.Strict);
            mockShaderRepo.Setup(x => x.GetByProfileIdAsync(It.IsAny<int>())).ReturnsAsync(new List<ShaderPack>());
            mockShaderRepo.Setup(x => x.CreateAsync(It.IsAny<ShaderPack>())).ReturnsAsync((ShaderPack pack) => { pack.Id = 1; return pack.Id; });
            mockShaderRepo.Setup(x => x.DeleteAsync(It.IsAny<int>())).Returns(Task.CompletedTask);

            var mockResourceRepo = new Mock<IResourcePackRepository>(MockBehavior.Strict);
            mockResourceRepo.Setup(x => x.GetByProfileIdAsync(It.IsAny<int>())).ReturnsAsync(new List<ResourcePack>());
            mockResourceRepo.Setup(x => x.CreateAsync(It.IsAny<ResourcePack>())).ReturnsAsync((ResourcePack pack) => { pack.Id = 1; return pack.Id; });
            mockResourceRepo.Setup(x => x.DeleteAsync(It.IsAny<int>())).Returns(Task.CompletedTask);

            var mockMapRepo = new Mock<IGameMapRepository>(MockBehavior.Strict);
            mockMapRepo.Setup(x => x.GetByProfileIdAsync(profile.Id)).ReturnsAsync(new List<GameMap>());
            mockMapRepo.Setup(x => x.CreateAsync(It.IsAny<GameMap>())).ReturnsAsync((GameMap map) => { map.Id = 1; return map.Id; });
            mockMapRepo.Setup(x => x.DeleteAsync(It.IsAny<int>())).Returns(Task.CompletedTask);

            var mockScriptRepo = new Mock<IScriptRepository>(MockBehavior.Strict);
            mockScriptRepo.Setup(x => x.GetByProfileIdAsync(profile.Id)).ReturnsAsync(new List<Script>());
            mockScriptRepo.Setup(x => x.CreateAsync(It.IsAny<Script>())).ReturnsAsync((Script script) => { script.Id = 1; return script.Id; });
            mockScriptRepo.Setup(x => x.DeleteAsync(It.IsAny<int>())).Returns(Task.CompletedTask);

            using var dataRootScope = TestPaths.UseLauncherDataRoot("ShoroCraftProfileBackupData", out var dataRoot);
            var backupsRoot = Path.Combine(dataRoot, "backups", profile.Name);
            if (Directory.Exists(backupsRoot))
                Directory.Delete(backupsRoot, true);

            var service = CreateProfileService(
                profileRepository: mockProfileRepo,
                modRepository: mockModRepo,
                shaderPackRepository: mockShaderRepo,
                resourcePackRepository: mockResourceRepo,
                gameMapRepository: mockMapRepo,
                scriptRepository: mockScriptRepo,
                minecraftService: mockMinecraft);

            await service.CreateBackupAsync(profile.Id, "Worlds");
            var backups = await service.GetBackupsAsync(profile.Id);

            Assert.Single(backups);
            Assert.Equal("Worlds", backups[0].BackupType);
            Assert.EndsWith(".zip", backups[0].FilePath);
            Assert.True(File.Exists(backups[0].FilePath));

            var worldDir = Path.Combine(savesDir, "SavedWorld");
            Assert.True(Directory.Exists(worldDir));
            Directory.Delete(worldDir, true);
            Assert.False(Directory.Exists(worldDir));

            await service.RestoreBackupAsync(profile.Id, backups[0].FilePath);
            Assert.True(Directory.Exists(worldDir));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}
