using Microsoft.EntityFrameworkCore;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Data.Database;

namespace ShoroCraftLauncher.Data.Repositories;

public class ProfileRepository : IProfileRepository
{
    private readonly LauncherDbContext _context;

    public ProfileRepository(LauncherDbContext context) => _context = context;

    public async Task<List<Profile>> GetAllAsync() =>
        await _context.Profiles.OrderByDescending(p => p.UpdatedAt).ToListAsync();

    public async Task<Profile?> GetByIdAsync(int id) =>
        await _context.Profiles.FindAsync(id);

    public async Task<int> CreateAsync(Profile profile)
    {
        profile.CreatedAt = DateTime.UtcNow;
        profile.UpdatedAt = DateTime.UtcNow;
        _context.Profiles.Add(profile);
        await _context.SaveChangesAsync();
        return profile.Id;
    }

    public async Task UpdateAsync(Profile profile)
    {
        profile.UpdatedAt = DateTime.UtcNow;
        _context.Profiles.Update(profile);
        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        var profile = await _context.Profiles.FindAsync(id);
        if (profile != null)
        {
            _context.Profiles.Remove(profile);
            await _context.SaveChangesAsync();
        }
    }
}
