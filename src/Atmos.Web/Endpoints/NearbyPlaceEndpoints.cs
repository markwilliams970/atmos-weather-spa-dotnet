using Atmos.Web.Models;
using Atmos.Web.Services;
using Serilog;

namespace Atmos.Web.Endpoints;

public static class NearbyPlaceEndpoints
{
    public static void MapNearbyPlaceEndpoints(this WebApplication app)
    {
        app.MapGet("/api/nearby-place", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        double? lat, double? lon, INearbyPlaceService nearbyPlace,
        IDiagnosticContext diagnosticContext, CancellationToken cancellationToken)
    {
        if (lat is null || lon is null)
        {
            return Results.BadRequest(new ApiErrorResponse("Invalid coordinates."));
        }

        diagnosticContext.Set("Latitude", lat.Value);
        diagnosticContext.Set("Longitude", lon.Value);

        var name = await nearbyPlace.FindNearbyPlaceNameAsync(lat.Value, lon.Value, cancellationToken);
        diagnosticContext.Set("Found", name is not null);

        return Results.Ok(new NearbyPlaceResponse(name));
    }
}

public sealed record NearbyPlaceResponse(string? Name);
