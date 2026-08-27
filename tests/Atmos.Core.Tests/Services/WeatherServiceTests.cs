using System.Net;
using Atmos.Core.Configuration;
using Atmos.Core.Models;
using Atmos.Core.Services;
using Atmos.Core.Tests.TestSupport;
using Microsoft.Extensions.Options;

namespace Atmos.Core.Tests.Services;

public class WeatherServiceTests
{
    private const string MinimalForecastJson = """
        {
          "current": { "time":"2026-08-27T14:00","temperature_2m":22.4,"apparent_temperature":21.8,"relative_humidity_2m":48,"wind_speed_10m":14.2,"wind_direction_10m":270,"precipitation":0.0,"uv_index":4.6,"weather_code":2,"is_day":1 },
          "hourly": { "time":["2026-08-27T14:00"],"temperature_2m":[22.4],"precipitation_probability":[10],"weather_code":[2],"is_day":[1] },
          "daily": { "time":["2026-08-27"],"temperature_2m_max":[23],"temperature_2m_min":[12],"weather_code":[2],"precipitation_sum":[0],"precipitation_probability_max":[10],"sunrise":["2026-08-27T06:22"],"sunset":["2026-08-27T19:47"],"uv_index_max":[6.1],"wind_speed_10m_max":[18.3] }
        }
        """;

    private static ExternalApiOptions TestOptions => new()
    {
        Zippopotam = "https://api.zippopotam.us",
        OpenMeteoForecast = "https://api.open-meteo.com",
        OpenMeteoGeocoding = "https://geocoding-api.open-meteo.com",
        OpenMeteoAirQuality = "https://air-quality-api.open-meteo.com",
        OpenMeteoElevation = "https://api.open-meteo.com",
        Overpass = "https://overpass-api.de",
        Nominatim = "https://nominatim.openstreetmap.org",
        NominatimUserAgent = "AtmosWeather-Tests/1.0",
        RainViewer = "https://api.rainviewer.com",
    };

    [Fact]
    public async Task GetForecastAsync_maps_response_into_a_shaped_forecast()
    {
        var client = FakeHttpMessageHandler.CreateJsonClient(MinimalForecastJson);
        var service = new WeatherService(client, Options.Create(TestOptions));

        var forecast = await service.GetForecastAsync(
            new Location("Boulder", "CO", 40.0150, -105.2705), elevationMeters: null, CancellationToken.None);

        Assert.Equal("Boulder, CO", forecast.Location);
        Assert.Equal(72, forecast.TempF);
    }

    [Fact]
    public async Task GetForecastAsync_includes_elevation_query_param_when_provided()
    {
        string? capturedUrl = null;
        var client = FakeHttpMessageHandler.CreateClient(req =>
        {
            capturedUrl = req.RequestUri!.ToString();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(MinimalForecastJson, System.Text.Encoding.UTF8, "application/json"),
            };
        });
        var service = new WeatherService(client, Options.Create(TestOptions));

        await service.GetForecastAsync(
            new Location("Boulder", "CO", 40.0150, -105.2705), elevationMeters: 1655.0, CancellationToken.None);

        Assert.Contains("elevation=1655", capturedUrl);
    }

    [Fact]
    public async Task GetForecastAsync_throws_weather_service_exception_on_failure_never_leaks_raw_error()
    {
        var client = FakeHttpMessageHandler.CreateClient(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var service = new WeatherService(client, Options.Create(TestOptions));

        var ex = await Assert.ThrowsAsync<WeatherServiceException>(() =>
            service.GetForecastAsync(new Location("Boulder", "CO", 40.0150, -105.2705), null, CancellationToken.None));

        Assert.Equal("Weather data is temporarily unavailable.", ex.Message);
    }
}
