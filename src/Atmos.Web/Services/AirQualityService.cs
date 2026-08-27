using System.Diagnostics;
using System.Net.Http.Json;
using Atmos.Core.Configuration;
using Atmos.Core.Conversions;
using Atmos.Core.Services;
using Atmos.Web.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atmos.Web.Services;

public sealed class AirQualityService(
    HttpClient httpClient,
    IOptions<ExternalApiOptions> options,
    ILogger<AirQualityService> logger) : IAirQualityService
{
    private readonly ExternalApiOptions _options = options.Value;

    public async Task<AirQuality> GetAirQualityAsync(
        double latitude, double longitude, CancellationToken cancellationToken)
    {
        var url = $"{_options.OpenMeteoAirQuality}/v1/air-quality" +
                  $"?latitude={latitude.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}" +
                  $"&longitude={longitude.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}" +
                  "&current=us_aqi,pm10,pm2_5,ozone,nitrogen_dioxide&timezone=auto";

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(url, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex,
                "Air quality lookup failed for {Lat},{Lon} after {ElapsedMs}ms",
                latitude, longitude, stopwatch.ElapsedMilliseconds);
            throw new AirQualityUnavailableException();
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "Air quality API returned {StatusCode} for {Lat},{Lon} in {ElapsedMs}ms",
                (int)response.StatusCode, latitude, longitude, stopwatch.ElapsedMilliseconds);
            throw new AirQualityUnavailableException();
        }

        var body = await response.Content.ReadFromJsonAsync<OpenMeteoAirQualityResponse>(cancellationToken);
        var current = body?.Current ?? throw new AirQualityUnavailableException();

        var aqi = (int)UnitConversions.JsRound(current.UsAqi);
        var (category, color) = AqiCategorizer.Categorize(aqi);

        logger.LogDebug(
            "Air quality for {Lat},{Lon} resolved to AQI {UsAqi} ({Category}) in {ElapsedMs}ms",
            latitude, longitude, aqi, category, stopwatch.ElapsedMilliseconds);

        return new AirQuality(
            UsAqi: aqi,
            Pm25: Math.Round(current.Pm25, 1),
            Pm10: Math.Round(current.Pm10, 1),
            Ozone: (int)UnitConversions.JsRound(current.Ozone),
            No2: (int)UnitConversions.JsRound(current.NitrogenDioxide),
            Category: category,
            Color: color);
    }
}

/// <summary>
/// Air quality is a non-essential enhancement (CLAUDE.md §12) — its failure must
/// never take down the core forecast. Callers catch this and render the card as
/// "Unavailable" rather than surfacing a 5xx for the whole page.
/// </summary>
public sealed class AirQualityUnavailableException : Exception;
