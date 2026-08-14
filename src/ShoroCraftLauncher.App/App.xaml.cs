using System.Globalization;
using System.Net.Http;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using ShoroCraftLauncher.App.ViewModels;
using ShoroCraftLauncher.App.Views;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Data.Database;
using ShoroCraftLauncher.Data.Repositories;
using ShoroCraftLauncher.Infrastructure.Authentication;
using ShoroCraftLauncher.Infrastructure.Minecraft;
using ShoroCraftLauncher.Infrastructure.Services;
using ShoroCraftLauncher.App.Services;

namespace ShoroCraftLauncher.App;

public partial class App : Application
{
    private readonly IHost _host;
    private ILogService? _logService;

    public App()
    {
        _host = CreateHostBuilder();
        DispatcherUnhandledException += App_DispatcherUnhandledException;
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        HandleCrash(e.Exception, "UI Thread");
        e.Handled = true;
    }

    private void HandleCrash(Exception ex, string source)
    {
        _logService?.Critical("App", source, "Crash", ex);
        Log.Fatal(ex, "Crash");
        
        try 
        {
            var crashPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"crash-{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            var report = $"ShoroCraft Launcher Crash Report\nDate: {DateTime.Now}\nSource: {source}\n\nException: {ex.GetType().Name}\nMessage: {ex.Message}\n\nStackTrace:\n{ex.StackTrace}";
            File.WriteAllText(crashPath, report);
            
            System.Windows.MessageBox.Show($"Ha ocurrido un error crítico.\n\nSe ha generado un reporte en:\n{crashPath}\n\nEl launcher se cerrará.", "ShoroCraft Crash Reporter", MessageBoxButton.OK, MessageBoxImage.Error);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", crashPath) { UseShellExecute = true });
        }
        catch { }
        
        Log.CloseAndFlush();
        Environment.Exit(1);
    }

    private static IHost CreateHostBuilder()
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShoroCraftLauncher", "data", "launcher.db");

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
            .WriteTo.File(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ShoroCraftLauncher", "logs", "launcher-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .WriteTo.Console()
            .CreateLogger();

        return Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices((_, services) =>
            {
                services.AddDbContextFactory<LauncherDbContext>(options =>
                    options.UseSqlite($"Data Source={dbPath}"));

                services.AddSingleton<DbInitializer>();
                services.AddSingleton(new HttpClient());
                services.AddSingleton<ShoroCraftLauncher.Infrastructure.Downloading.IResumableDownloadService,
                    ShoroCraftLauncher.Infrastructure.Downloading.ResumableDownloadService>();

                services.AddSingleton<IProfileRepository, ProfileRepository>();
                services.AddSingleton<IModRepository, ModRepository>();
                services.AddSingleton<IResourcePackRepository, ResourcePackRepository>();
                services.AddSingleton<IShaderPackRepository, ShaderPackRepository>();
                services.AddSingleton<IScriptRepository, ScriptRepository>();
                services.AddSingleton<IGameMapRepository, GameMapRepository>();
                services.AddSingleton<IGameVersionRepository, GameVersionRepository>();
                services.AddSingleton<ISettingsRepository, SettingsRepository>();
                services.AddSingleton<IServerRepository, ServerRepository>();

                services.AddSingleton<ILogService, LogService>();
                services.AddSingleton<IMinecraftService, MinecraftService>();
                services.AddSingleton<IJavaService, JavaService>();
                services.AddSingleton<IAuthenticationService, AuthenticationService>();
                services.AddSingleton<ILauncherService, LauncherService>();
                services.AddSingleton<IModService, ModService>();
                services.AddSingleton<IResourcePackService, ResourcePackService>();
                services.AddSingleton<IShaderPackService, ShaderPackService>();
                services.AddSingleton<IScriptService, ScriptService>();
                services.AddSingleton<IGameMapService, GameMapService>();
                services.AddSingleton<IModpackService, ModpackService>();
                services.AddSingleton<IProfileService, ProfileService>();
                services.AddSingleton<IServerService, ServerService>();
                services.AddSingleton<IDialogService, DialogService>();
                services.AddSingleton<IUpdaterService, UpdaterService>();

                services.AddTransient<DashboardViewModel>();
                services.AddTransient<ProfilesViewModel>();
                services.AddTransient<ModsViewModel>();
                services.AddTransient<ResourcePacksViewModel>();
                services.AddTransient<ShaderPacksViewModel>();
                services.AddTransient<ScriptsViewModel>();
                services.AddTransient<MapsViewModel>();
                services.AddTransient<ServersViewModel>();
                services.AddSingleton<ConsoleViewModel>();
                services.AddTransient<SettingsViewModel>();

                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        try
        {
            base.OnStartup(e);

            await _host.StartAsync();
            _logService = _host.Services.GetRequiredService<ILogService>();
            _logService.Info("App", "Started", "ShoroCraft Launcher iniciado.");

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                    HandleCrash(ex, "AppDomain");
            };

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                // Las tareas en segundo plano no deben tumbar la aplicación: se registran y se marcan como observadas.
                _logService?.Error("App", "UnobservedTaskException",
                    "Excepción no controlada en una tarea en segundo plano.", args.Exception);
                Log.Error(args.Exception, "Unobserved task exception");
                args.SetObserved();
            };

            using (var scope = _host.Services.CreateScope())
            {
                var initializer = scope.ServiceProvider.GetRequiredService<DbInitializer>();
                initializer.Initialize();

                var settingsRepo = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
                var savedLanguage = await settingsRepo.GetAsync("language");
                AddLocaleDictionary(savedLanguage);
            }

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            _logService?.Critical("App", "StartupFailed", "Falló el arranque de la aplicación.", ex);
            Log.Fatal(ex, "Application start-up failed");
            System.Windows.MessageBox.Show($"Critical error during startup:\n{ex.Message}\n\nCheck logs for details.", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void AddLocaleDictionary(string? savedLanguage)
    {
        var locale = ResolveLocale(savedLanguage);
        var localeDict = new ResourceDictionary { Source = new Uri($"Locales/{locale}.xaml", UriKind.Relative) };
        Application.Current.Resources.MergedDictionaries.Add(localeDict);
    }

    private static string ResolveLocale(string? savedLanguage)
    {
        if (savedLanguage is "es" or "en")
            return savedLanguage == "es" ? "es-ES" : "en-US";

        // Idiomas guardados sin recurso disponible (fr/de/pt) o sin idioma guardado:
        // se usa la cultura del sistema con respaldo a inglés.
        var culture = CultureInfo.CurrentUICulture;
        return culture.Name.StartsWith("es") ? "es-ES" : "en-US";
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        _logService?.Info("App", "Shutdown", "ShoroCraft Launcher cerrando.");
        try
        {
            var serverService = _host.Services.GetService<IServerService>();
            if (serverService != null)
                await serverService.StopAllAsync();
        }
        catch (Exception ex)
        {
            _logService?.Error("App", "Shutdown", "Error deteniendo servidores durante el cierre.", ex);
        }
        if (_logService != null)
            await _logService.FlushAsync();
        await _host.StopAsync();
        Log.CloseAndFlush();
    }
}
