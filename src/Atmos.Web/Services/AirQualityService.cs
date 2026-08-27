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

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(url, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Air quality lookup failed for {Lat},{Lon}", latitude, longitude);
            throw new AirQualityUnavailableException();
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Air quality API returned {Status}", response.StatusCode);
            throw new AirQualityUnavailableException();
        }

        var body = await response.Content.ReadFromJsonAsync<OpenMeteoAirQualityResponse>(cancellationToken);
        var current = body?.Current ?? throw new AirQualityUnavailableException();

        var aqi = (int)UnitConversions.JsRound(current.UsAqi);
        var (category, color) = AqiCategorizer.Categorize(aqi);

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
