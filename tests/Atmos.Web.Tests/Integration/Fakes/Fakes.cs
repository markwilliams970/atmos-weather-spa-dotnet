using Atmos.Core.Models;
using Atmos.Core.Services;
using Atmos.Web.Models;
using Atmos.Web.Services;

namespace Atmos.Web.Tests.Integration.Fakes;

/// <summary>
/// Deterministic stand-ins for every external-API-backed service, registered
/// over the real (HttpClient-based) ones in <see cref="AtmosWebApplicationFactory"/>.
/// The HTTP call logic itself is already covered by the fixture-based unit
/// tests in Services/ — these integration tests exist to exercise routing,
/// model binding, session handling and persistence, not to re-verify external
/// API parsing (CLAUDE.md §18: prefer deterministic fixtures over live
/// Internet dependencies in the test suite).
/// </summary>
internal sealed class FakeGeocodingService : IGeocodingService
{
    public static readonly Location Arvada = new("Arvada", "CO", 39.8283, -105.0844);

    public Task<Location?> LookupZipAsync(string zip, CancellationToken cancellationToken) =>
        Task.FromResult(zip == "80002" ? Arvada : null);

    public Task<IReadOnlyList<GeocodeResult>> SearchCityAsync(string query, int count, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GeocodeResult>>(
            query.Contains("Denver", StringComparison.OrdinalIgnoreCase)
                ? [new GeocodeResult("Denver", "Colorado", "US", 39.7392, -104.9847)]
                : []);
}

internal sealed class FakeWeatherService : IWeatherService
{
    /// <summary>A location city named this triggers a simulated upstream failure, for testing the 502 path.</summary>
    public const string FailingCity = "trigger-weather-failure";

    public Task<WeatherForecast> GetForecastAsync(Location location, double? elevationMeters, CancellationToken cancellationToken)
    {
        if (location.City == FailingCity)
        {
            throw new WeatherServiceException("Simulated upstream failure.");
        }

        return Task.FromResult(new WeatherForecast(
            Location: $"{location.City}, {location.State}",
            Zip: "",
            Latitude: location.Latitude,
            Longitude: location.Longitude,
            TempF: 72, TempC: 22,
            FeelsLikeF: 70, FeelsLikeC: 21,
            Humidity: 45,
            WindMph: 8, WindKmh: 13,
            WindDir: "NW", WindDeg: 315,
            PrecipIn: 0, PrecipMm: 0,
            UvIndex: 5.0,
            Condition: "Clear sky", ConditionEmoji: "☀️",
            Sunrise: "6:30 AM", Sunset: "7:45 PM",
            SunriseMin: 390, SunsetMin: 1185,
            IsDay: true,
            TodayHighF: 78, TodayHighC: 26,
            TodayLowF: 55, TodayLowC: 13,
            Hourly: [new HourlySlot("Now", true, 72, 22, 10, "☀️", true)],
            Daily: [new DailyRow("Today", "Jan 1", 78, 26, 55, 13, "☀️", "Clear sky", 0, 0, 10, 5.0, 12, 19)],
            ElevationMeters: elevationMeters));
    }
}

internal sealed class FakeElevationService : IElevationService
{
    public Task<double?> GetElevationAsync(double latitude, double longitude, CancellationToken cancellationToken) =>
        Task.FromResult<double?>(1655.0);
}

internal sealed class FakeNearbyPlaceService : INearbyPlaceService
{
    public Task<string?> FindNearbyPlaceNameAsync(double latitude, double longitude, CancellationToken cancellationToken) =>
        Task.FromResult<string?>("Table Mountain");
}

internal sealed class FakeAirQualityService : IAirQualityService
{
    public Task<AirQuality> GetAirQualityAsync(double latitude, double longitude, CancellationToken cancellationToken) =>
        Task.FromResult(new AirQuality(42, 10.5, 18.2, 30, 12, "Good", "#00e400"));
}

internal sealed class FakeRadarService : IRadarService
{
    public Task<RadarFrame?> GetLatestFrameAsync(CancellationToken cancellationToken) =>
        Task.FromResult<RadarFrame?>(new RadarFrame("https://tilecache.rainviewer.com", "/v2/radar/1234567890", DateTimeOffset.UtcNow));
}
