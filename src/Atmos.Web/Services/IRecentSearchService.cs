using Atmos.Web.Data;

namespace Atmos.Web.Services;

public interface IRecentSearchService
{
    /// <summary>Last N searches for a session, most recent first.</summary>
    Task<IReadOnlyList<RecentSearch>> GetRecentAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Upserts by (SessionId, Label) and trims to the configured max-per-session
    /// in the same transaction — replaces the reference app's three-statement
    /// delete/insert/trim (weather-server.ts saveSearch) with one set-based
    /// operation (Phase B §7).
    /// </summary>
    Task SaveAsync(
        string sessionId,
        string label,
        double latitude,
        double longitude,
        double? elevationMeters,
        UnitsPreference units,
        LocationType locationType,
        CancellationToken cancellationToken);

    /// <summary>
    /// No-op if no row matches — mirrors handleSaveUnits' plain UPDATE, which
    /// silently does nothing for an unknown label rather than erroring.
    /// </summary>
    Task UpdateUnitsAsync(string sessionId, string label, UnitsPreference units, CancellationToken cancellationToken);
}
