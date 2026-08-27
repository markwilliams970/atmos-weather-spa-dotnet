using Atmos.Web.Models;
using Atmos.Web.Services;
using Serilog;

namespace Atmos.Web.Endpoints;

public static class AirQualityEndpoints
{
    public static void MapAirQualityEndpoints(this WebApplication app)
    {
        app.MapGet("/api/air-quality", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        double? lat, double? lon, IAirQualityService airQuality,
        IDiagnosticContext diagnosticContext, CancellationToken cancellationToken)
    {
        if (lat is null || lon is null)
        {
            return Results.BadRequest(new ApiErrorResponse("Invalid coordinates."));
        }

        diagnosticContext.Set("Latitude", lat.Value);
        diagnosticContext.Set("Longitude", lon.Value);

        try
        {
            var result = await airQuality.GetAirQualityAsync(lat.Value, lon.Value, cancellationToken);
            diagnosticContext.Set("UsAqi", result.UsAqi);
            return Results.Ok(result);
        }
        catch (AirQualityUnavailableException)
        {
            return Results.Json(
                new ApiErrorResponse("Air quality data is temporarily unavailable."),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
