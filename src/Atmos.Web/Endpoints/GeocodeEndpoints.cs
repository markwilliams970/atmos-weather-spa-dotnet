using Atmos.Core.Services;
using Atmos.Web.Models;
using Serilog;

namespace Atmos.Web.Endpoints;

public static class GeocodeEndpoints
{
    public static void MapGeocodeEndpoints(this WebApplication app)
    {
        app.MapGet("/api/geocode", HandleAsync);
    }

    private static async Task<GeocodeResponse> HandleAsync(
        IGeocodingService geocoding, IDiagnosticContext diagnosticContext,
        CancellationToken cancellationToken, string? q, int count = 5)
    {
        // A non-nullable value-type query parameter with no default is treated
        // as *required* by Minimal API binding — count=5 is the reference app's
        // own default (weather-server.ts handleGeocode), and the client (this
        // repo's own search.js) legitimately omits it on the autocomplete path.
        diagnosticContext.Set("Query", q ?? "");

        var results = await geocoding.SearchCityAsync(q ?? "", count, cancellationToken);
        diagnosticContext.Set("ResultCount", results.Count);

        return new GeocodeResponse(results);
    }
}
