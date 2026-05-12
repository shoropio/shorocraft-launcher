using Microsoft.EntityFrameworkCore;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Data.Database;

namespace ShoroCraftLauncher.Data.Repositories;

public class ModRepository : IModRepository
{
    private readonly LauncherDbContext _context;

    public ModRepository(LauncherDbContext context) => _context = context;

    public async Task<List<Mod>> GetByProfileIdAsync(int profileId) =>
        await _context.Mods.Where(m => m.ProfileId == profileId).OrderByDescending(m => m.AddedAt).ToListAsync();

    public async Task<Mod?> GetByIdAsync(int id) =>
        await _context.Mods.FindAsync(id);

    public async Task<int> CreateAsync(Mod mod)
    {
        mod.AddedAt = DateTime.UtcNow;
        _context.Mods.Add(mod);
        await _context.SaveChangesAsync();
        return mod.Id;
    }

    public async Task UpdateAsync(Mod mod)
    {
        _context.Mods.Update(mod);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var mod = await _context.Mods.FindAsync(id);
        if (mod != null)
        {
            _context.Mods.Remove(mod);
            await _context.SaveChangesAsync();
        }
    }

    public async Task DeleteByProfileIdAsync(int profileId)
    {
        var mods = await _context.Mods.Where(m => m.ProfileId == profileId).ToListAsync();
        _context.Mods.RemoveRange(mods);
        await _context.SaveChangesAsync();
    }
}
