using System.Text.RegularExpressions;
using Atmos.Core.Models;
using Atmos.Core.Services;
using Atmos.Web.Data;
using Atmos.Web.Data.Configurations;
using Atmos.Web.Infrastructure;
using Atmos.Web.Models;
using Atmos.Web.Services;

namespace Atmos.Web.Endpoints;

/// <summary>
/// Ported from handleWeather (weather-server.ts:2260-2294): accepts either
/// zip= or lat=&amp;lon=&amp;label=, saves the resolved search to Recent as a
/// side effect. The GET-with-a-side-effect shape is preserved (it's the read
/// operation the UI actually needs); only /api/save-units' pure mutation
/// moved to PUT, per CLAUDE.md §16.
/// </summary>
public static partial class WeatherEndpoints
{
    [GeneratedRegex(@"^\d{5}$")]
    private static partial Regex ZipFormat();

    public static void MapWeatherEndpoints(this WebApplication app)
    {
        app.MapGet("/api/weather", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        string? zip,
        string? lat,
        string? lon,
        string? label,
        string? units,
        string? elevation,
        string? locationType,
        IGeocodingService geocoding,
        IWeatherService weather,
        IRecentSearchService recentSearch,
        IAppSessionAccessor session,
        CancellationToken cancellationToken)
    {
        var unitsPreference = units == "metric" ? UnitsPreference.Metric : UnitsPreference.Imperial;

        Location location;
        string displayLabel;
        var zipForDisplay = "";
        LocationType resolvedLocationType;

        if (!string.IsNullOrEmpty(zip))
        {
            if (!ZipFormat().IsMatch(zip))
            {
                return Results.BadRequest(new ApiErrorResponse("Invalid ZIP code."));
            }

            var resolved = await geocoding.LookupZipAsync(zip, cancellationToken);
            if (resolved is null)
            {
                return Results.BadRequest(new ApiErrorResponse($"ZIP code \"{zip}\" not found."));
            }

            location = resolved;
            displayLabel = $"{resolved.City}, {resolved.State}";
            zipForDisplay = zip;
            resolvedLocationType = LocationType.Zip;
        }
        else if (!string.IsNullOrEmpty(lat) && !string.IsNullOrEmpty(lon) && !string.IsNullOrEmpty(label))
        {
            if (!double.TryParse(lat, out var la) || !double.TryParse(lon, out var lo)
                || la is < -90 or > 90 || lo is < -180 or > 180)
            {
                return Results.BadRequest(new ApiErrorResponse("Invalid coordinates."));
            }

            if (label.Length > RecentSearchConfiguration.MaxLabelLength)
            {
                return Results.BadRequest(new ApiErrorResponse("Location label is too long."));
            }

            location = new Location(label, "", la, lo);
            displayLabel = label;
            resolvedLocationType = locationType == "map" ? LocationType.Map : LocationType.City;
        }
        else
        {
            return Results.BadRequest(new ApiErrorResponse("Provide zip or lat/lon/label."));
        }

        var elevationMeters = double.TryParse(elevation, out var e) ? e : (double?)null;

        var forecast = await weather.GetForecastAsync(location, elevationMeters, cancellationToken);
        forecast = forecast with { Location = displayLabel, Zip = zipForDisplay };

        await recentSearch.SaveAsync(
            session.SessionId, displayLabel, location.Latitude, location.Longitude,
            elevationMeters, unitsPreference, resolvedLocationType, cancellationToken);

        return Results.Ok(forecast);
    }
}
