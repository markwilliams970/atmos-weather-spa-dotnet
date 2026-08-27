using Atmos.Web.Models;
using Atmos.Web.Services;

namespace Atmos.Web.Endpoints;

public static class AirQualityEndpoints
{
    public static void MapAirQualityEndpoints(this WebApplication app)
    {
        app.MapGet("/api/air-quality", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        double? lat, double? lon, IAirQualityService airQuality, CancellationToken cancellationToken)
    {
        if (lat is null || lon is null)
        {
            return Results.BadRequest(new ApiErrorResponse("Invalid coordinates."));
        }

        try
        {
            var result = await airQuality.GetAirQualityAsync(lat.Value, lon.Value, cancellationToken);
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
