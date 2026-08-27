using Atmos.Core.Models;

namespace Atmos.Core.Services;

public interface IWeatherService
{
    /// <summary>
    /// Fetches and shapes a full current/hourly/daily forecast for a location.
    /// When <paramref name="elevationMeters"/> is supplied, Open-Meteo downscales
    /// temperature for that specific altitude rather than its native grid cell
    /// (mirrors weather-server.ts fetchWeather's optional "elevation" param).
    /// </summary>
    Task<WeatherForecast> GetForecastAsync(
        Location location,
        double? elevationMeters,
        CancellationToken cancellationToken);
}
