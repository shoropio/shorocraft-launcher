using Microsoft.EntityFrameworkCore;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Data.Database;

namespace ShoroCraftLauncher.Data.Repositories;

public class ProfileRepository : IProfileRepository
{
    private readonly IDbContextFactory<LauncherDbContext> _contextFactory;

    public ProfileRepository(IDbContextFactory<LauncherDbContext> contextFactory) => _contextFactory = contextFactory;

    public async Task<List<Profile>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Profiles.AsNoTracking().OrderByDescending(p => p.UpdatedAt).ToListAsync();
    }

    public async Task<Profile?> GetByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<int> CreateAsync(Profile profile)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        profile.CreatedAt = DateTime.UtcNow;
        profile.UpdatedAt = DateTime.UtcNow;
        context.Profiles.Add(profile);
        await context.SaveChangesAsync();
        return profile.Id;
    }

    public async Task UpdateAsync(Profile profile)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        profile.UpdatedAt = DateTime.UtcNow;
        context.Profiles.Update(profile);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync();
        var profile = await context.Profiles.FindAsync(id);
        if (profile != null)
        {
            context.Profiles.Remove(profile);
            await context.SaveChangesAsync();
        }
    }
}
