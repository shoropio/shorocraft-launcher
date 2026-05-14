using Microsoft.Data.Sqlite;
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
        MigrateSchema();
        SeedDefaults();
    }

    /// <summary>
    /// Applies incremental schema changes for columns added after the initial DB creation.
    /// Safe to call on every startup — each ALTER is wrapped in a column-existence check.
    /// </summary>
    private void MigrateSchema()
    {
        var conn = _context.Database.GetDbConnection();
        conn.Open();
        try
        {
            using var cmd = conn.CreateCommand();

            // Check and add Description column to Mods
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Mods') WHERE name='Description'";
            var hasDesc = (long)(cmd.ExecuteScalar() ?? 0L);
            if (hasDesc == 0)
            {
                cmd.CommandText = "ALTER TABLE Mods ADD COLUMN Description TEXT NULL";
                cmd.ExecuteNonQuery();
            }

            // Check and add IconPath column to Mods
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Mods') WHERE name='IconPath'";
            var hasIcon = (long)(cmd.ExecuteScalar() ?? 0L);
            if (hasIcon == 0)
            {
                cmd.CommandText = "ALTER TABLE Mods ADD COLUMN IconPath TEXT NULL";
                cmd.ExecuteNonQuery();
            }
        }
        finally
        {
            conn.Close();
        }
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
