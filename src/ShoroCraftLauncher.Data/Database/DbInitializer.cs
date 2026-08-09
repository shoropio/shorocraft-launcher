using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Data.Database;

public class DbInitializer
{
    private readonly IDbContextFactory<LauncherDbContext> _contextFactory;

    public DbInitializer(IDbContextFactory<LauncherDbContext> contextFactory) => _contextFactory = contextFactory;

    public void Initialize()
    {
        using var context = _contextFactory.CreateDbContext();
        context.Database.EnsureCreated();
        MigrateSchema(context);
        SeedDefaults(context);
    }

    /// <summary>
    /// Applies incremental schema changes for columns added after the initial DB creation.
    /// Safe to call on every startup — each ALTER is wrapped in a column-existence check.
    /// </summary>
    private static void MigrateSchema(LauncherDbContext context)
    {
        var conn = context.Database.GetDbConnection();
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

            // Create GameMaps table if it doesn't exist
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS GameMaps (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ProfileId INTEGER NOT NULL,
                    Name TEXT NOT NULL,
                    FileName TEXT NOT NULL,
                    FilePath TEXT NOT NULL,
                    FileSizeBytes INTEGER NOT NULL,
                    PreviewImagePath TEXT NULL,
                    Status TEXT NOT NULL,
                    AddedAt TEXT NOT NULL
                )";
            cmd.ExecuteNonQuery();

            EnsureGameMapsStatusText(conn);
        }
        finally
        {
            conn.Close();
        }
    }

    private static void EnsureGameMapsStatusText(System.Data.Common.DbConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT type FROM pragma_table_info('GameMaps') WHERE name='Status'";
        var statusType = (cmd.ExecuteScalar()?.ToString() ?? string.Empty).Trim();

        if (statusType.Equals("TEXT", StringComparison.OrdinalIgnoreCase))
            return;

        cmd.CommandText = @"
            CREATE TABLE GameMaps_New (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProfileId INTEGER NOT NULL,
                Name TEXT NOT NULL,
                FileName TEXT NOT NULL,
                FilePath TEXT NOT NULL,
                FileSizeBytes INTEGER NOT NULL,
                PreviewImagePath TEXT NULL,
                Status TEXT NOT NULL,
                AddedAt TEXT NOT NULL
            )";
        cmd.ExecuteNonQuery();

        cmd.CommandText = @"
            INSERT INTO GameMaps_New (Id, ProfileId, Name, FileName, FilePath, FileSizeBytes, PreviewImagePath, Status, AddedAt)
            SELECT Id, ProfileId, Name, FileName, FilePath, FileSizeBytes, PreviewImagePath,
                   CASE
                       WHEN Status = 0 THEN 'Active'
                       WHEN Status = 1 THEN 'Inactive'
                       ELSE COALESCE(CAST(Status AS TEXT), 'Active')
                   END,
                   AddedAt
            FROM GameMaps";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "DROP TABLE GameMaps";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "ALTER TABLE GameMaps_New RENAME TO GameMaps";
        cmd.ExecuteNonQuery();

        cmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_GameMaps_ProfileId ON GameMaps (ProfileId)";
        cmd.ExecuteNonQuery();
    }

    private static void SeedDefaults(LauncherDbContext context)
    {
        if (context.LauncherSettings.Any()) return;

        context.LauncherSettings.AddRange(
            new LauncherSetting { Key = "theme", Value = "dark" },
            new LauncherSetting { Key = "default_min_ram", Value = "1024" },
            new LauncherSetting { Key = "default_max_ram", Value = "4096" },
            new LauncherSetting { Key = "game_directory", Value = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft") },
            new LauncherSetting { Key = "close_launcher_on_launch", Value = "false" },
            new LauncherSetting { Key = "keep_launcher_open", Value = "true" },
            new LauncherSetting { Key = "language", Value = "es" },
            new LauncherSetting { Key = "launcher_version", Value = "1.2.0" }
        );

        context.SaveChanges();
    }
}
