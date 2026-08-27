namespace Atmos.Web.Services;

public interface INearbyPlaceService
{
    /// <summary>
    /// Nearest named geographic feature (peak, river, town, etc.) to a point, for
    /// labeling map-picked locations that have no place name of their own.
    /// Returns null on any failure or empty result — deliberately does not retry
    /// or widen the search (a cosmetic label enhancement, CLAUDE.md §12).
    /// </summary>
    Task<string?> FindNearbyPlaceNameAsync(double latitude, double longitude, CancellationToken cancellationToken);
}
