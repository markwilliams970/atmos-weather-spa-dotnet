using Atmos.Web.Models;
using Atmos.Web.Services;

namespace Atmos.Web.Endpoints;

public static class NearbyPlaceEndpoints
{
    public static void MapNearbyPlaceEndpoints(this WebApplication app)
    {
        app.MapGet("/api/nearby-place", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        double? lat, double? lon, INearbyPlaceService nearbyPlace, CancellationToken cancellationToken)
    {
        if (lat is null || lon is null)
        {
            return Results.BadRequest(new ApiErrorResponse("Invalid coordinates."));
        }

        var name = await nearbyPlace.FindNearbyPlaceNameAsync(lat.Value, lon.Value, cancellationToken);
        return Results.Ok(new NearbyPlaceResponse(name));
    }
}

public sealed record NearbyPlaceResponse(string? Name);
