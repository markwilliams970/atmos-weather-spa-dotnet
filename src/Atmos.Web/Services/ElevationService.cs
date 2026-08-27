using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Atmos.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atmos.Web.Services;

public sealed class ElevationService(
    HttpClient httpClient,
    IOptions<ExternalApiOptions> options,
    ILogger<ElevationService> logger) : IElevationService
{
    private readonly ExternalApiOptions _options = options.Value;

    public async Task<double?> GetElevationAsync(
        double latitude, double longitude, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var url = $"{_options.OpenMeteoElevation}/v1/elevation" +
                      $"?latitude={latitude.ToString("R", CultureInfo.InvariantCulture)}" +
                      $"&longitude={longitude.ToString("R", CultureInfo.InvariantCulture)}";

            var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "Elevation lookup for {Lat},{Lon} returned {StatusCode} in {ElapsedMs}ms",
                    latitude, longitude, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<ElevationResponse>(cancellationToken);
            var elevation = body?.Elevation is { Count: > 0 } values ? values[0] : (double?)null;

            logger.LogDebug(
                "Elevation lookup for {Lat},{Lon} resolved to {ElevationMeters}m in {ElapsedMs}ms",
                latitude, longitude, elevation, stopwatch.ElapsedMilliseconds);
            return elevation;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex,
                "Elevation lookup failed for {Lat},{Lon} after {ElapsedMs}ms",
                latitude, longitude, stopwatch.ElapsedMilliseconds);
            return null;
        }
    }

    private sealed class ElevationResponse
    {
        [JsonPropertyName("elevation")]
        public List<double>? Elevation { get; set; }
    }
}
