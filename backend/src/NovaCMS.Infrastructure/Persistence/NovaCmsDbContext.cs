using Microsoft.EntityFrameworkCore;
using NovaCMS.Domain.Authentication;

namespace NovaCMS.Infrastructure.Persistence;

public sealed class NovaCmsDbContext(DbContextOptions<NovaCmsDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NovaCmsDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
