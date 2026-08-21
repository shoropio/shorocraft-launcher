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
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        return await context.Profiles.AsNoTracking().OrderByDescending(p => p.UpdatedAt).ToListAsync().ConfigureAwait(false);
    }

    public async Task<Profile?> GetByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        return await context.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id).ConfigureAwait(false);
    }

    public async Task<int> CreateAsync(Profile profile)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        profile.CreatedAt = DateTime.UtcNow;
        profile.UpdatedAt = DateTime.UtcNow;
        context.Profiles.Add(profile);
        await context.SaveChangesAsync().ConfigureAwait(false);
        return profile.Id;
    }

    public async Task UpdateAsync(Profile profile)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        profile.UpdatedAt = DateTime.UtcNow;
        context.Profiles.Update(profile);
        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        var profile = await context.Profiles.FindAsync(id).ConfigureAwait(false);
        if (profile != null)
        {
            context.Profiles.Remove(profile);
            await context.SaveChangesAsync().ConfigureAwait(false);
        }
    }
}
