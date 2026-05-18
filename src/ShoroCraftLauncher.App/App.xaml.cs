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
        _logService?.Critical("App", "UiUnhandledException", "Excepción no controlada en UI.", e.Exception);
        Log.Fatal(e.Exception, "Unhandled exception occurred");
        System.Windows.MessageBox.Show($"Ha ocurrido un error inesperado:\n{e.Exception.Message}\n\nEl launcher se cerrará por seguridad.", "Error Inesperado", MessageBoxButton.OK, MessageBoxImage.Error);
        Log.CloseAndFlush();
        e.Handled = true;
        Shutdown(1);
    }

    private static IHost CreateHostBuilder()
    {
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ShoroCraftLauncher", "data", "launcher.db");

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
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
                services.AddHttpClient();

                services.AddSingleton<IProfileRepository, ProfileRepository>();
                services.AddSingleton<IModRepository, ModRepository>();
                services.AddSingleton<IResourcePackRepository, ResourcePackRepository>();
                services.AddSingleton<IShaderPackRepository, ShaderPackRepository>();
                services.AddSingleton<IScriptRepository, ScriptRepository>();
                services.AddSingleton<IGameMapRepository, GameMapRepository>();
                services.AddSingleton<IGameVersionRepository, GameVersionRepository>();
                services.AddSingleton<ISettingsRepository, SettingsRepository>();

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
                services.AddSingleton<IProfileService, ProfileService>();
                services.AddSingleton<IDialogService, DialogService>();

                services.AddTransient<DashboardViewModel>();
                services.AddTransient<ProfilesViewModel>();
                services.AddTransient<ModsViewModel>();
                services.AddTransient<ResourcePacksViewModel>();
                services.AddTransient<ShaderPacksViewModel>();
                services.AddTransient<ScriptsViewModel>();
                services.AddTransient<MapsViewModel>();
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
                    _logService?.Critical("App", "UnhandledException", "Excepción no controlada del dominio.", ex);
            };

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                _logService?.Error("App", "UnobservedTaskException", "Excepción no observada en tarea.", args.Exception);
                args.SetObserved();
            };

            using (var scope = _host.Services.CreateScope())
            {
                var initializer = scope.ServiceProvider.GetRequiredService<DbInitializer>();
                initializer.Initialize();
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

    protected override async void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        _logService?.Info("App", "Shutdown", "ShoroCraft Launcher cerrando.");
        if (_logService != null)
            await _logService.FlushAsync();
        await _host.StopAsync();
        Log.CloseAndFlush();
    }
}
