using System.Net.Http.Json;
using Atmos.Core.Configuration;
using Atmos.Web.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atmos.Web.Services;

public sealed class RadarService(
    HttpClient httpClient,
    IOptions<ExternalApiOptions> options,
    ILogger<RadarService> logger) : IRadarService
{
    private readonly ExternalApiOptions _options = options.Value;
    private const string DefaultHost = "https://tilecache.rainviewer.com";

    public async Task<RadarFrame?> GetLatestFrameAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await httpClient.GetAsync(
                $"{_options.RainViewer}/public/weather-maps.json", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<RainViewerResponse>(cancellationToken);
            var frame = body?.Radar?.Past?.LastOrDefault();
            if (frame is null)
            {
                return null;
            }

            // Must use host + frame.path, never a raw timestamp — RainViewer's API
            // moved away from timestamp-based tile URLs; building URLs from a raw
            // "time" value silently 410s. See docs/phase-a-assessment.md §4.
            return new RadarFrame(
                Host: body?.Host ?? DefaultHost,
                Path: frame.Path,
                FrameTimeUtc: DateTimeOffset.FromUnixTimeSeconds(frame.Time));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "RainViewer frame lookup failed");
            return null;
        }
    }
}
