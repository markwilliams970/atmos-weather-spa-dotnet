using Atmos.Web.Models;
using Atmos.Web.Services;

namespace Atmos.Web.Endpoints;

public static class ElevationEndpoints
{
    public static void MapElevationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/elevation", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        double? lat, double? lon, IElevationService elevation, CancellationToken cancellationToken)
    {
        if (lat is null || lon is null)
        {
            return Results.BadRequest(new ApiErrorResponse("Invalid coordinates."));
        }

        var result = await elevation.GetElevationAsync(lat.Value, lon.Value, cancellationToken);
        return Results.Ok(new ElevationResponse(result));
    }
}

public sealed record ElevationResponse(double? Elevation);
