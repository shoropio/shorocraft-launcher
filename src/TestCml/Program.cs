using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using ShoroCraftLauncher.Core.Enums;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Data.Database;
using ShoroCraftLauncher.Data.Repositories;
using ShoroCraftLauncher.Infrastructure.Minecraft;
using ShoroCraftLauncher.Infrastructure.Services;

// ============================================================
// Harness de prueba en vivo:
//  1) Crea un perfil Fabric (ultima release), lo instala y lo lanza.
//  2) Crea un servidor Paper, lo descarga, lo inicia y lo detiene.
// Todo el progreso se imprime en esta consola.
// ============================================================

var testRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "ShoroCraftLauncher", "livetest");
Directory.CreateDirectory(testRoot);

Log("========== SHOROCRAFT LIVE TEST ==========");
Log($"Raiz de pruebas: {testRoot}");

// ---------- Infraestructura básica ----------
var httpClient = new HttpClient();
var logService = new LogService();
var launcherLogger = new SimpleLogger<object>();

var javaService = new JavaService(new SimpleLogger<JavaService>(), httpClient);
var minecraftService = new MinecraftService(new SimpleLogger<MinecraftService>(), httpClient, logService);
var authService = new StubAuthService();
var launcherService = new LauncherService(
    minecraftService, javaService, authService,
    new SimpleLogger<LauncherService>(), logService);

launcherService.LogOutput += m => Log("[LAUNCHER-CONSOLE] " + m);
launcherService.ProgressChanged += (pct, msg) => Log($"[PROGRESS {pct:0}%] {msg}");
launcherService.ProgressCompleted += () => Log("[PROGRESS] Completado.");
launcherService.GameExited += code => Log($"[GAME] Salió con código {code}.");

int failures = 0;

// ---------- PARTE 1: Perfil Fabric ----------
try
{
    Section("PARTE 1: Perfil Fabric");

    Log("Resolviendo ultima version release de Minecraft...");
    var mcVersion = await minecraftService.ResolveVersionIdAsync("latest");
    Log($"Minecraft release: {mcVersion}");

    Log("Obteniendo la ultima version de Fabric loader...");
    var loaderVersion = await minecraftService.ResolveLatestLoaderVersionAsync("fabric", mcVersion);
    Log($"Fabric loader: {loaderVersion}");

    Log("Buscando Java recomendado...");
    var javaPath = await javaService.GetRecommendedJavaPathAsync(mcVersion);
    if (string.IsNullOrEmpty(javaPath))
    {
        Log("Java no encontrado. Descargando (puede tardar)...");
        javaPath = await javaService.DownloadJavaForVersionAsync(
            mcVersion, new Progress<double>(p => Log($"  Descargando Java: {p:0}%")));
    }
    Log($"Java: {javaPath}");

    var profile = new Profile
    {
        Name = "LiveFabric",
        Type = ProfileType.Fabric,
        MinecraftVersion = mcVersion,
        LoaderVersion = loaderVersion,
        GameDirectory = Path.Combine(testRoot, "fabric-game"),
        MinRamMB = 1024,
        MaxRamMB = 2048
    };
    Directory.CreateDirectory(profile.GameDirectory);

    Log($"Instalando Fabric {loaderVersion} en {profile.GameDirectory} ...");
    await minecraftService.InstallLoaderAsync(
        mcVersion, "fabric", loaderVersion, javaPath,
        msg => Log($"[INSTALL] {msg}"),
        new Progress<double>(p => Log($"[INSTALL-PROGRESS] {p:0}%")),
        onLog: m => Log($"[INSTALL-LOG] {m}"),
        gameDir: profile.GameDirectory);
    Log("Fabric instalado.");

    Log("Lanzando el juego con sesion offline...");
    var result = await launcherService.LaunchProfileAsync(profile,
        authService.AuthenticateOfflineAsync("ShoroTester"));

    if (!result.Success)
    {
        Fail($"El lanzamiento fallo: {result.ErrorMessage}");
        failures++;
    }
    else
    {
        Log($"Proceso lanzado (PID {result.ProcessId}). Esperando 60s para ver vida del proceso...");
        var waited = 0;
        while (waited < 60 && launcherService.IsGameRunning)
        {
            await Task.Delay(5000);
            waited += 5;
            Log($"  ...proceso corriendo ({waited}s)");
        }

        if (waited >= 60) Log("El juego sobrevivio 60s. Deteniendo...");
        else Log("El proceso termino solo antes de 60s (revisar logs arriba).");

        await launcherService.StopGameAsync();
        Log("Proceso detenido.");
        if (waited < 15) { Fail("El juego murio demasiado pronto."); failures++; }
    }
}
catch (Exception ex)
{
    Fail($"Excepcion en parte Fabric: {ex}");
    failures++;
}

// ---------- PARTE 2: Servidor Paper ----------
ServerService? serverService = null;
MinecraftServer? server = null;
try
{
    Section("PARTE 2: Servidor Paper");

    var dbPath = Path.Combine(testRoot, "livetest.db");
    var options = new DbContextOptionsBuilder<LauncherDbContext>()
        .UseSqlite($"Data Source={dbPath}")
        .Options;
    await using (var ctx = new LauncherDbContext(options))
        await ctx.Database.EnsureCreatedAsync();
    var factory = new TestDbContextFactory(options);

    serverService = new ServerService(
        new ServerRepository(factory), minecraftService, javaService,
        httpClient, new SimpleLogger<ServerService>(), logService);
    await serverService.LoadAsync();

    serverService.LogOutput += m => Log("[SERVER] " + m);
    serverService.ProgressChanged += (pct, msg) => Log($"[SERVER-PROGRESS {pct:0}%] {msg32(msg, pct, 0)}");
    serverService.StatusChanged += st => Log($"[SERVER-STATUS] {st}");

    Log("Obteniendo versiones de Paper...");
    var paperVersions = await serverService.GetAvailablePaperVersionsAsync();
    var paperVersion = paperVersions.FirstOrDefault(v => !v.Contains('-', StringComparison.Ordinal))
        ?? paperVersions.First();
    Log($"Version Paper elegida: {paperVersion} (total {paperVersions.Count})");

    Log("Creando servidor Paper (descarga del jar incluida)...");
    server = await serverService.CreateServerAsync("PaperLiveTest", ServerType.Paper, paperVersion, 2048, onlineMode: false);
    Log($"Servidor creado id={server.Id} dir={server.DirectoryPath}");

    Log("Iniciando servidor...");
    var start = await serverService.StartAsync(server);
    if (!start.Success)
    {
        Fail($"No se pudo iniciar el servidor: {start.ErrorMessage}");
        failures++;
    }
    else
    {
        Log($"Servidor iniciado (PID {start.ProcessId}). Esperando a que termine de cargar (max 6 min)...");
        var deadline = DateTime.UtcNow.AddMinutes(6);
        var doneSeen = false;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(3000);
            var log = serverService.GetLogHistory(server);
            if (log.Any(l => l.Contains("Done (", StringComparison.OrdinalIgnoreCase)))
            {
                doneSeen = true;
                break;
            }
            if (!serverService.IsRunning(server)) break;
        }

        if (!serverService.IsRunning(server)) { Fail("El servidor murio antes de terminar de cargar."); failures++; }
        else if (!doneSeen) { Fail("Timeout: el servidor no reporto 'Done' en 6 minutos."); failures++; }
        else
        {
            Log("Servidor arriba. Enviando comando 'say Live test OK'...");
            await serverService.SendCommandAsync(server, "say Live test OK");
            await Task.Delay(2000);
        }

        Log("Deteniendo servidor...");
        await serverService.StopAsync(server);
        Log("Servidor detenido.");
    }
}
catch (Exception ex)
{
    Fail($"Excepcion en parte Paper: {ex}");
    failures++;
}

Section("RESUMEN");
Log(failures == 0 ? "TODO OK: Fabric y Paper pasaron la prueba en vivo." : $"Fallaron {failures} comprobaciones.");
return failures;

// ----------------- helpers -----------------
static string msg32(string msg, double pct, int _) => msg; // evita warning de parametro no usado

static void Log(string msg) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {msg}");
static void Section(string s) { Console.WriteLine(); Console.WriteLine($"===== {s} ====="); }
static void Fail(string msg) => Console.WriteLine($"[FAIL] {msg}");

class SimpleLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (logLevel >= LogLevel.Information)
            Console.WriteLine($"[SRV {typeof(T).Name}] {formatter(state, exception)}");
    }
}

class StubAuthService : IAuthenticationService
{
    public Task<AuthResult> AuthenticateAsync() => Task.FromResult(AuthenticateOfflineAsync("ShoroTester"));
    public Task<AuthResult> AuthenticateSilentlyAsync() => Task.FromResult(AuthenticateOfflineAsync("ShoroTester"));
    public AuthResult AuthenticateOfflineAsync(string username) => new()
    {
        Success = true,
        IsOffline = true,
        AccessToken = "offline",
        Uuid = Guid.NewGuid().ToString(),
        Username = username
    };
    public Task<bool> ValidateTokenAsync(string accessToken) => Task.FromResult(true);
    public Task<AuthResult> ValidateAndRefreshAsync(AuthResult current) => Task.FromResult(current);
    public Task LogoutAsync() => Task.CompletedTask;
}

class TestDbContextFactory(DbContextOptions<LauncherDbContext> options) : IDbContextFactory<LauncherDbContext>
{
    public LauncherDbContext CreateDbContext() => new(options);
    public Task<LauncherDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new LauncherDbContext(options));
}
