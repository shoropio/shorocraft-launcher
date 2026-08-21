using Microsoft.EntityFrameworkCore;
using ShoroCraftLauncher.Core.Interfaces;
using ShoroCraftLauncher.Core.Models;
using ShoroCraftLauncher.Data.Database;

namespace ShoroCraftLauncher.Data.Repositories;

public class ServerRepository : IServerRepository
{
    private readonly IDbContextFactory<LauncherDbContext> _contextFactory;

    public ServerRepository(IDbContextFactory<LauncherDbContext> contextFactory) => _contextFactory = contextFactory;

    public async Task<List<MinecraftServer>> GetAllAsync()
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        return await context.MinecraftServers.AsNoTracking().OrderBy(s => s.Name).ToListAsync().ConfigureAwait(false);
    }

    public async Task<MinecraftServer?> GetByIdAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        return await context.MinecraftServers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id).ConfigureAwait(false);
    }

    public async Task<int> CreateAsync(MinecraftServer server)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        server.CreatedAt = DateTime.UtcNow;
        server.UpdatedAt = DateTime.UtcNow;
        context.MinecraftServers.Add(server);
        await context.SaveChangesAsync().ConfigureAwait(false);
        return server.Id;
    }

    public async Task UpdateAsync(MinecraftServer server)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        server.UpdatedAt = DateTime.UtcNow;
        context.MinecraftServers.Update(server);
        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    public async Task DeleteAsync(int id)
    {
        await using var context = await _contextFactory.CreateDbContextAsync().ConfigureAwait(false);
        var server = await context.MinecraftServers.FindAsync(id).ConfigureAwait(false);
        if (server != null)
        {
            context.MinecraftServers.Remove(server);
            await context.SaveChangesAsync().ConfigureAwait(false);
        }
    }
}
