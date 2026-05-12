using Microsoft.EntityFrameworkCore;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Data.Database;

namespace ShoroCraftLauncher.Data.Repositories;

public class ShaderPackRepository : IShaderPackRepository
{
    private readonly LauncherDbContext _context;

    public ShaderPackRepository(LauncherDbContext context) => _context = context;

    public async Task<List<ShaderPack>> GetByProfileIdAsync(int profileId) =>
        await _context.ShaderPacks.Where(s => s.ProfileId == profileId).OrderByDescending(s => s.AddedAt).ToListAsync();

    public async Task<ShaderPack?> GetByIdAsync(int id) =>
        await _context.ShaderPacks.FindAsync(id);

    public async Task<int> CreateAsync(ShaderPack pack)
    {
        pack.AddedAt = DateTime.UtcNow;
        _context.ShaderPacks.Add(pack);
        await _context.SaveChangesAsync();
        return pack.Id;
    }

    public async Task UpdateAsync(ShaderPack pack)
    {
        _context.ShaderPacks.Update(pack);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var pack = await _context.ShaderPacks.FindAsync(id);
        if (pack != null)
        {
            _context.ShaderPacks.Remove(pack);
            await _context.SaveChangesAsync();
        }
    }
}
