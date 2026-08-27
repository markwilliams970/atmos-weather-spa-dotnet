using System.Net.Http.Json;
using Atmos.Core.Configuration;
using Atmos.Core.Models;
using Microsoft.Extensions.Options;

namespace Atmos.Core.Services;

public sealed class WeatherService(HttpClient httpClient, IOptions<ExternalApiOptions> options) : IWeatherService
{
    private readonly ExternalApiOptions _options = options.Value;

    public async Task<WeatherForecast> GetForecastAsync(
        Location location, double? elevationMeters, CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string>
        {
            ["latitude"] = location.Latitude.ToString("R"),
            ["longitude"] = location.Longitude.ToString("R"),
            ["current"] = "temperature_2m,apparent_temperature,relative_humidity_2m,wind_speed_10m,wind_direction_10m,precipitation,uv_index,weather_code,is_day",
            ["hourly"] = "temperature_2m,precipitation_probability,weather_code,is_day",
            ["daily"] = "temperature_2m_max,temperature_2m_min,weather_code,precipitation_sum,precipitation_probability_max,sunrise,sunset,uv_index_max,wind_speed_10m_max",
            ["temperature_unit"] = "celsius",
            ["wind_speed_unit"] = "kmh",
            ["precipitation_unit"] = "mm",
            ["timezone"] = "auto",
            ["forecast_days"] = "7",
        };

        if (elevationMeters is { } elevation && !double.IsNaN(elevation))
        {
            query["elevation"] = elevation.ToString("R");
        }

        var queryString = string.Join('&', query.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        var url = $"{_options.OpenMeteoForecast}/v1/forecast?{queryString}";

        var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new WeatherServiceException("Weather data is temporarily unavailable.");
        }

        var body = await response.Content.ReadFromJsonAsync<OpenMeteoForecastResponse>(cancellationToken)
            ?? throw new WeatherServiceException("Weather data is temporarily unavailable.");

        return ForecastMapper.Map(body, location, elevationMeters);
    }
}
