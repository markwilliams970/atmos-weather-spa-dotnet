using Atmos.Web.Configuration;
using Atmos.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Atmos.Web.Services;

public sealed class RecentSearchService(AtmosDbContext db, IOptions<RecentSearchOptions> options) : IRecentSearchService
{
    private readonly int _maxPerSession = options.Value.MaxPerSession;

    public async Task<IReadOnlyList<RecentSearch>> GetRecentAsync(string sessionId, CancellationToken cancellationToken) =>
        // AsNoTracking is required here, not just a perf nicety: UpdateUnitsAsync
        // and SaveAsync's trim step both use EF Core's bulk ExecuteUpdate/
        // ExecuteDelete, which bypass the change tracker. A tracking query in the
        // same DbContext scope would resolve to a stale cached entity instead of
        // the just-written database row (a real bug this test suite caught).
        await db.RecentSearches
            .AsNoTracking()
            .Where(r => r.SessionId == sessionId)
            .OrderByDescending(r => r.LastAccessedUtc)
            .Take(_maxPerSession)
            .ToListAsync(cancellationToken);

    public async Task SaveAsync(
        string sessionId,
        string label,
        double latitude,
        double longitude,
        double? elevationMeters,
        UnitsPreference units,
        LocationType locationType,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var existing = await db.RecentSearches
            .FirstOrDefaultAsync(r => r.SessionId == sessionId && r.Label == label, cancellationToken);

        if (existing is not null)
        {
            existing.Latitude = latitude;
            existing.Longitude = longitude;
            existing.ElevationMeters = elevationMeters;
            existing.Units = units;
            existing.LocationType = locationType;
            existing.LastAccessedUtc = now;
        }
        else
        {
            db.RecentSearches.Add(new RecentSearch
            {
                SessionId = sessionId,
                Label = label,
                Latitude = latitude,
                Longitude = longitude,
                ElevationMeters = elevationMeters,
                Units = units,
                LocationType = locationType,
                CreatedUtc = now,
                LastAccessedUtc = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        var idsToKeep = await db.RecentSearches
            .Where(r => r.SessionId == sessionId)
            .OrderByDescending(r => r.LastAccessedUtc)
            .Take(_maxPerSession)
            .Select(r => r.Id)
            .ToListAsync(cancellationToken);

        await db.RecentSearches
            .Where(r => r.SessionId == sessionId && !idsToKeep.Contains(r.Id))
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateUnitsAsync(
        string sessionId, string label, UnitsPreference units, CancellationToken cancellationToken)
    {
        await db.RecentSearches
            .Where(r => r.SessionId == sessionId && r.Label == label)
            .ExecuteUpdateAsync(setters => setters.SetProperty(r => r.Units, units), cancellationToken);
    }
}
