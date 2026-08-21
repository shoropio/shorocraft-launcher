using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Data.Database;
using ShoroCraftLauncher.Data.Repositories;
using Xunit;

namespace ShoroCraftLauncher.Tests;

/// <summary>
/// Pruebas E2E de la capa de datos real: SQLite en archivo temporal,
/// DbInitializer completo y repositorios reales (sin mocks de persistencia).
/// </summary>
public class EndToEndDataTests
{
    private sealed class FakeSecretStorage : ISecretStorage
    {
        public Dictionary<string, string> Secrets { get; } = new();

        public Task SetSecretAsync(string key, string value)
        {
            Secrets[key] = value;
            return Task.CompletedTask;
        }

        public Task<string?> GetSecretAsync(string key)
            => Task.FromResult(Secrets.TryGetValue(key, out var value) ? value : null);

        public Task<bool> HasSecretAsync(string key)
            => Task.FromResult(Secrets.ContainsKey(key));

        public Task DeleteSecretAsync(string key)
        {
            Secrets.Remove(key);
            return Task.CompletedTask;
        }
    }

    private static IDbContextFactory<LauncherDbContext> CreateFactory(string dbPath)
    {
        var options = new DbContextOptionsBuilder<LauncherDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        return new DbContextFactoryWrapper(options);
    }

    private sealed class DbContextFactoryWrapper : IDbContextFactory<LauncherDbContext>
    {
        private readonly DbContextOptions<LauncherDbContext> _options;
        public DbContextFactoryWrapper(DbContextOptions<LauncherDbContext> options) => _options = options;
        public LauncherDbContext CreateDbContext() => new(_options);
        public Task<LauncherDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }

    [Fact]
    public async Task FullStack_InitializeSeedProfileAndSettings_RoundTrips()
    {
        using var dataRootScope = TestPaths.UseLauncherDataRoot("ShoroCraftE2E", out _);
        var dbPath = Path.Combine(TestPaths.CreateTempDir("ShoroCraftE2EDb"), "launcher.db");

        var factory = CreateFactory(dbPath);
        new DbInitializer(factory).Initialize();

        // 1. Seed defaults presentes tras inicializar
        var secretStorage = new FakeSecretStorage();
        var settingsRepo = new SettingsRepository(
            factory, secretStorage, Mock.Of<ILogger<SettingsRepository>>());

        var all = await settingsRepo.GetAllAsync();
        Assert.Contains("theme", all.Keys);
        Assert.Equal("dark", all["theme"]);

        // 2. Ajuste normal: persiste en base de datos
        await settingsRepo.SetAsync("theme", "light");
        Assert.Equal("light", await settingsRepo.GetAsync("theme"));

        // 3. API key: se enruta al almacenamiento seguro y NUNCA a la base de datos
        const string apiKey = "cf-secret-value-123";
        await settingsRepo.SetAsync("curseforge_api_key", apiKey);

        Assert.Equal(apiKey, await settingsRepo.GetAsync("curseforge_api_key"));
        Assert.Equal(apiKey, secretStorage.Secrets["curseforge_api_key"]);

        var afterKey = await settingsRepo.GetAllAsync();
        Assert.False(afterKey.ContainsKey("curseforge_api_key"), "La API key no debe aparecer en GetAllAsync");

        await using (var verifyContext = factory.CreateDbContext())
        {
            var rowInDb = await verifyContext.LauncherSettings
                .FirstOrDefaultAsync(s => s.Key == "curseforge_api_key");
            Assert.Null(rowInDb);
        }

        // 4. Perfiles: crear y recuperar con el repositorio real
        var profileRepo = new ProfileRepository(factory);
        var profile = new Profile
        {
            Name = "E2E Profile",
            MinecraftVersion = "1.21.4",
            Type = ProfileType.Fabric,
            MinRamMB = 2048,
            MaxRamMB = 8192,
            WindowWidth = 854,
            WindowHeight = 480
        };
        var id = await profileRepo.CreateAsync(profile);
        Assert.True(id > 0);

        var loaded = await profileRepo.GetByIdAsync(id);
        Assert.NotNull(loaded);
        Assert.Equal("E2E Profile", loaded!.Name);
        Assert.Equal(ProfileType.Fabric, loaded.Type);
        Assert.Equal(8192, loaded.MaxRamMB);

        var profiles = await profileRepo.GetAllAsync();
        Assert.Contains(profiles, p => p.Id == id);

        // 5. Actualizar y verificar
        loaded.MaxRamMB = 10240;
        await profileRepo.UpdateAsync(loaded);
        var reloaded = await profileRepo.GetByIdAsync(id);
        Assert.NotNull(reloaded);
        Assert.Equal(10240, reloaded!.MaxRamMB);
    }

    [Fact]
    public async Task FullStack_SecretDelete_RemovesFromSecureStorage()
    {
        using var dataRootScope = TestPaths.UseLauncherDataRoot("ShoroCraftE2ESecret", out _);
        var dbPath = Path.Combine(TestPaths.CreateTempDir("ShoroCraftE2ESecretDb"), "launcher.db");

        var factory = CreateFactory(dbPath);
        new DbInitializer(factory).Initialize();

        var secretStorage = new FakeSecretStorage();
        var settingsRepo = new SettingsRepository(
            factory, secretStorage, Mock.Of<ILogger<SettingsRepository>>());

        await settingsRepo.SetAsync("curseforge_api_key", "to-delete");
        Assert.True(secretStorage.Secrets.ContainsKey("curseforge_api_key"));

        await secretStorage.DeleteSecretAsync("curseforge_api_key");

        Assert.Null(await settingsRepo.GetAsync("curseforge_api_key"));
    }
}
