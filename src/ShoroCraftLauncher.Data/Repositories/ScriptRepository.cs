using Microsoft.EntityFrameworkCore;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Data.Database;

namespace ShoroCraftLauncher.Data.Repositories;

public class ScriptRepository : IScriptRepository
{
    private readonly LauncherDbContext _context;

    public ScriptRepository(LauncherDbContext context) => _context = context;

    public async Task<List<Script>> GetByProfileIdAsync(int profileId) =>
        await _context.Scripts.Where(s => s.ProfileId == profileId).OrderByDescending(s => s.ModifiedAt).ToListAsync();

    public async Task<Script?> GetByIdAsync(int id) =>
        await _context.Scripts.FindAsync(id);

    public async Task<int> CreateAsync(Script script)
    {
        script.CreatedAt = DateTime.UtcNow;
        script.ModifiedAt = DateTime.UtcNow;
        _context.Scripts.Add(script);
        await _context.SaveChangesAsync();
        return script.Id;
    }

    public async Task UpdateAsync(Script script)
    {
        script.ModifiedAt = DateTime.UtcNow;
        _context.Scripts.Update(script);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var script = await _context.Scripts.FindAsync(id);
        if (script != null)
        {
            _context.Scripts.Remove(script);
            await _context.SaveChangesAsync();
        }
    }
}
