using Atmos.Core.Models;

namespace Atmos.Core.Services;

public interface IGeocodingService
{
    /// <summary>
    /// Resolves a 5-digit US ZIP code to a location. Returns null if the ZIP
    /// isn't found (mirrors the reference app's "ZIP code not found" case,
    /// weather-server.ts lookupZip) rather than throwing for that case.
    /// </summary>
    Task<Location?> LookupZipAsync(string zip, CancellationToken cancellationToken);

    /// <summary>
    /// City-name autocomplete. Returns an empty list on any failure — this call
    /// backs live-typing suggestions and must never surface an error to the user.
    /// </summary>
    Task<IReadOnlyList<GeocodeResult>> SearchCityAsync(string query, int count, CancellationToken cancellationToken);
}
