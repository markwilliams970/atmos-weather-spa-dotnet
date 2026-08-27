namespace Atmos.Web.Services;

public interface IElevationService
{
    /// <summary>
    /// Ground elevation in meters for a coordinate. Returns null on any failure —
    /// this is a cosmetic/enhancement lookup and must never block the forecast
    /// (CLAUDE.md §12).
    /// </summary>
    Task<double?> GetElevationAsync(double latitude, double longitude, CancellationToken cancellationToken);
}
