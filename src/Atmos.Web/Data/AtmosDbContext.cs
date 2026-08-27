using Microsoft.EntityFrameworkCore;

namespace Atmos.Web.Data;

public sealed class AtmosDbContext(DbContextOptions<AtmosDbContext> options) : DbContext(options)
{
    public DbSet<RecentSearch> RecentSearches => Set<RecentSearch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AtmosDbContext).Assembly);
    }
}
