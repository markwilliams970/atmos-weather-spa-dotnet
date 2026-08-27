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
        try
        {
            var url = $"{_options.OpenMeteoElevation}/v1/elevation" +
                      $"?latitude={latitude.ToString("R", CultureInfo.InvariantCulture)}" +
                      $"&longitude={longitude.ToString("R", CultureInfo.InvariantCulture)}";

            var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<ElevationResponse>(cancellationToken);
            return body?.Elevation is { Count: > 0 } values ? values[0] : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Elevation lookup failed for {Lat},{Lon}", latitude, longitude);
            return null;
        }
    }

    private sealed class ElevationResponse
    {
        [JsonPropertyName("elevation")]
        public List<double>? Elevation { get; set; }
    }
}
