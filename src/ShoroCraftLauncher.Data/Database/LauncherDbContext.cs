using Microsoft.EntityFrameworkCore;
using ShoroCraftLauncher.Core.Models;

namespace ShoroCraftLauncher.Data.Database;

public class LauncherDbContext : DbContext
{
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<Mod> Mods => Set<Mod>();
    public DbSet<ResourcePack> ResourcePacks => Set<ResourcePack>();
    public DbSet<ShaderPack> ShaderPacks => Set<ShaderPack>();
    public DbSet<Script> Scripts => Set<Script>();
    public DbSet<GameVersion> GameVersions => Set<GameVersion>();
    public DbSet<LauncherSetting> LauncherSettings => Set<LauncherSetting>();
    public DbSet<DownloadHistory> DownloadHistories => Set<DownloadHistory>();
    public DbSet<LogEntry> LogEntries => Set<LogEntry>();

    public LauncherDbContext(DbContextOptions<LauncherDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Profile>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).IsRequired().HasMaxLength(100);
            e.Property(p => p.MinecraftVersion).IsRequired().HasMaxLength(50);
            e.Property(p => p.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(p => p.GameDirectory).HasMaxLength(500);
            e.Property(p => p.JavaPath).HasMaxLength(500);
            e.Property(p => p.JvmArguments).HasMaxLength(2000);
            e.Property(p => p.IconPath).HasMaxLength(500);
            e.Property(p => p.LoaderVersion).HasMaxLength(50);
        });

        modelBuilder.Entity<Mod>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Name).IsRequired().HasMaxLength(200);
            e.Property(m => m.FileName).IsRequired().HasMaxLength(255);
            e.Property(m => m.FilePath).IsRequired().HasMaxLength(500);
            e.Property(m => m.MinecraftVersion).HasMaxLength(50);
            e.Property(m => m.ModVersion).HasMaxLength(50);
            e.Property(m => m.Status).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(m => m.ProfileId);
        });

        modelBuilder.Entity<ResourcePack>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Name).IsRequired().HasMaxLength(200);
            e.Property(r => r.FileName).IsRequired().HasMaxLength(255);
            e.Property(r => r.FilePath).IsRequired().HasMaxLength(500);
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(r => r.PreviewImagePath).HasMaxLength(500);
            e.HasIndex(r => r.ProfileId);
        });

        modelBuilder.Entity<ShaderPack>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Name).IsRequired().HasMaxLength(200);
            e.Property(s => s.FileName).IsRequired().HasMaxLength(255);
            e.Property(s => s.FilePath).IsRequired().HasMaxLength(500);
            e.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(s => s.ProfileId);
        });

        modelBuilder.Entity<Script>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Name).IsRequired().HasMaxLength(200);
            e.Property(s => s.FileName).IsRequired().HasMaxLength(255);
            e.Property(s => s.FilePath).IsRequired().HasMaxLength(500);
            e.Property(s => s.BackupPath).HasMaxLength(500);
            e.HasIndex(s => s.ProfileId);
        });

        modelBuilder.Entity<GameVersion>(e =>
        {
            e.HasKey(g => g.Id);
            e.Property(g => g.VersionId).IsRequired().HasMaxLength(50);
            e.Property(g => g.VersionType).HasMaxLength(20);
            e.Property(g => g.Url).HasMaxLength(500);
            e.HasIndex(g => g.VersionId).IsUnique();
        });

        modelBuilder.Entity<LauncherSetting>(e =>
        {
            e.HasKey(s => s.Key);
            e.Property(s => s.Key).HasMaxLength(100);
            e.Property(s => s.Value).HasMaxLength(2000);
        });

        modelBuilder.Entity<DownloadHistory>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.FileName).HasMaxLength(255);
            e.Property(d => d.Url).HasMaxLength(1000);
        });

        modelBuilder.Entity<LogEntry>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Message).IsRequired();
            e.Property(l => l.Level).HasConversion<string>().HasMaxLength(20);
            e.Property(l => l.Source).HasMaxLength(100);
            e.HasIndex(l => l.ProfileId);
        });
    }
}
