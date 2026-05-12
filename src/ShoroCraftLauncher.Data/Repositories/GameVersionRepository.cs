using Microsoft.EntityFrameworkCore;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Data.Database;

namespace ShoroCraftLauncher.Data.Repositories;

public class GameVersionRepository : IGameVersionRepository
{
    private readonly LauncherDbContext _context;

    public GameVersionRepository(LauncherDbContext context) => _context = context;

    public async Task<List<GameVersion>> GetAllAsync() =>
        await _context.GameVersions.OrderByDescending(g => g.ReleasedAt).ToListAsync();

    public async Task<GameVersion?> GetByIdAsync(int id) =>
        await _context.GameVersions.FindAsync(id);

    public async Task<GameVersion?> GetByVersionIdAsync(string versionId) =>
        await _context.GameVersions.FirstOrDefaultAsync(g => g.VersionId == versionId);

    public async Task<int> CreateAsync(GameVersion version)
    {
        _context.GameVersions.Add(version);
        await _context.SaveChangesAsync();
        return version.Id;
    }

    public async Task UpdateAsync(GameVersion version)
    {
        version.UpdatedAt = DateTime.UtcNow;
        _context.GameVersions.Update(version);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var version = await _context.GameVersions.FindAsync(id);
        if (version != null)
        {
            _context.GameVersions.Remove(version);
            await _context.SaveChangesAsync();
        }
    }
}
