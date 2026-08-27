using Atmos.Web.Models;
using Atmos.Web.Services;
using Serilog;

namespace Atmos.Web.Endpoints;

public static class ElevationEndpoints
{
    public static void MapElevationEndpoints(this WebApplication app)
    {
        app.MapGet("/api/elevation", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        double? lat, double? lon, IElevationService elevation,
        IDiagnosticContext diagnosticContext, CancellationToken cancellationToken)
    {
        if (lat is null || lon is null)
        {
            return Results.BadRequest(new ApiErrorResponse("Invalid coordinates."));
        }

        diagnosticContext.Set("Latitude", lat.Value);
        diagnosticContext.Set("Longitude", lon.Value);

        var result = await elevation.GetElevationAsync(lat.Value, lon.Value, cancellationToken);
        if (result is not null)
        {
            diagnosticContext.Set("ElevationMeters", result.Value);
        }

        return Results.Ok(new ElevationResponse(result));
    }
}

public sealed record ElevationResponse(double? Elevation);
