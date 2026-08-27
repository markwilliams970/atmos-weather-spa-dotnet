using Atmos.Web.Configuration;
using Atmos.Web.Data;
using Atmos.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Atmos.Web.Tests.Services;

/// <summary>
/// Uses EF Core's SQLite relational provider (not the pure in-memory provider,
/// which doesn't enforce constraints/transactions realistically) as a fast,
/// dependency-free stand-in for SQL Server — per Phase B §16's acknowledged
/// small fidelity gap, verified against real SQL Server during Phase C.
/// </summary>
public sealed class RecentSearchServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private AtmosDbContext _db = null!;
    private RecentSearchService _service = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<AtmosDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AtmosDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _service = new RecentSearchService(_db, Options.Create(new RecentSearchOptions { MaxPerSession = 10 }));
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task SaveAsync_inserts_a_new_search()
    {
        await _service.SaveAsync(
            "session-a", "Boulder, CO", 40.0150, -105.2705, null,
            UnitsPreference.Imperial, LocationType.City, CancellationToken.None);

        var recent = await _service.GetRecentAsync("session-a", CancellationToken.None);

        var row = Assert.Single(recent);
        Assert.Equal("Boulder, CO", row.Label);
        Assert.Equal(LocationType.City, row.LocationType);
    }

    [Fact]
    public async Task SaveAsync_upserts_by_session_and_label_rather_than_duplicating()
    {
        await _service.SaveAsync("s1", "Boulder, CO", 40.0, -105.0, null, UnitsPreference.Imperial, LocationType.City, CancellationToken.None);
        await _service.SaveAsync("s1", "Boulder, CO", 40.1, -105.1, 1600, UnitsPreference.Metric, LocationType.Map, CancellationToken.None);

        var recent = await _service.GetRecentAsync("s1", CancellationToken.None);

        var row = Assert.Single(recent);
        Assert.Equal(40.1, row.Latitude);
        Assert.Equal(1600, row.ElevationMeters);
        Assert.Equal(UnitsPreference.Metric, row.Units);
    }

    [Fact]
    public async Task SaveAsync_trims_to_max_per_session_keeping_most_recently_accessed()
    {
        var service = new RecentSearchService(_db, Options.Create(new RecentSearchOptions { MaxPerSession = 3 }));

        for (var i = 0; i < 5; i++)
        {
            await service.SaveAsync("s1", $"Place {i}", i, i, null, UnitsPreference.Imperial, LocationType.Zip, CancellationToken.None);
            await Task.Delay(5); // ensure distinct LastAccessedUtc ordering
        }

        var recent = await service.GetRecentAsync("s1", CancellationToken.None);

        Assert.Equal(3, recent.Count);
        Assert.Equal(["Place 4", "Place 3", "Place 2"], recent.Select(r => r.Label));
    }

    [Fact]
    public async Task GetRecentAsync_only_returns_rows_for_the_requesting_session()
    {
        await _service.SaveAsync("s1", "A", 1, 1, null, UnitsPreference.Imperial, LocationType.Zip, CancellationToken.None);
        await _service.SaveAsync("s2", "B", 2, 2, null, UnitsPreference.Imperial, LocationType.Zip, CancellationToken.None);

        var recentS1 = await _service.GetRecentAsync("s1", CancellationToken.None);

        var row = Assert.Single(recentS1);
        Assert.Equal("A", row.Label);
    }

    [Fact]
    public async Task UpdateUnitsAsync_updates_matching_row_and_is_a_noop_for_unknown_label()
    {
        await _service.SaveAsync("s1", "Boulder, CO", 40.0, -105.0, null, UnitsPreference.Imperial, LocationType.City, CancellationToken.None);

        await _service.UpdateUnitsAsync("s1", "Boulder, CO", UnitsPreference.Metric, CancellationToken.None);
        await _service.UpdateUnitsAsync("s1", "Nonexistent", UnitsPreference.Metric, CancellationToken.None); // must not throw

        var recent = await _service.GetRecentAsync("s1", CancellationToken.None);
        Assert.Equal(UnitsPreference.Metric, Assert.Single(recent).Units);
    }
}
