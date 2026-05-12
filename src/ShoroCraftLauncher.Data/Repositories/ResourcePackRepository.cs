using Microsoft.EntityFrameworkCore;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Data.Database;

namespace ShoroCraftLauncher.Data.Repositories;

public class ResourcePackRepository : IResourcePackRepository
{
    private readonly LauncherDbContext _context;

    public ResourcePackRepository(LauncherDbContext context) => _context = context;

    public async Task<List<ResourcePack>> GetByProfileIdAsync(int profileId) =>
        await _context.ResourcePacks.Where(r => r.ProfileId == profileId).OrderByDescending(r => r.AddedAt).ToListAsync();

    public async Task<ResourcePack?> GetByIdAsync(int id) =>
        await _context.ResourcePacks.FindAsync(id);

    public async Task<int> CreateAsync(ResourcePack pack)
    {
        pack.AddedAt = DateTime.UtcNow;
        _context.ResourcePacks.Add(pack);
        await _context.SaveChangesAsync();
        return pack.Id;
    }

    public async Task UpdateAsync(ResourcePack pack)
    {
        _context.ResourcePacks.Update(pack);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var pack = await _context.ResourcePacks.FindAsync(id);
        if (pack != null)
        {
            _context.ResourcePacks.Remove(pack);
            await _context.SaveChangesAsync();
        }
    }
}
