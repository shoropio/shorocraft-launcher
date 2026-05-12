using Microsoft.EntityFrameworkCore;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Data.Database;

public class DbInitializer
{
    private readonly LauncherDbContext _context;

    public DbInitializer(LauncherDbContext context) => _context = context;

    public void Initialize()
    {
        _context.Database.EnsureCreated();
        SeedDefaults();
    }

    private void SeedDefaults()
    {
        if (_context.LauncherSettings.Any()) return;

        _context.LauncherSettings.AddRange(
            new LauncherSetting { Key = "theme", Value = "dark" },
            new LauncherSetting { Key = "default_min_ram", Value = "1024" },
            new LauncherSetting { Key = "default_max_ram", Value = "4096" },
            new LauncherSetting { Key = "game_directory", Value = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft") },
            new LauncherSetting { Key = "close_launcher_on_launch", Value = "false" },
            new LauncherSetting { Key = "keep_launcher_open", Value = "true" },
            new LauncherSetting { Key = "language", Value = "es" },
            new LauncherSetting { Key = "launcher_version", Value = "1.0.0" }
        );

        _context.SaveChanges();
    }
}
