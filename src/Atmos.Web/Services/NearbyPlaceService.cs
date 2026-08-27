using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using Atmos.Core.Configuration;
using Atmos.Core.Conversions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atmos.Web.Services;

public sealed class NearbyPlaceService(
    HttpClient httpClient,
    IOptions<ExternalApiOptions> options,
    ILogger<NearbyPlaceService> logger) : INearbyPlaceService
{
    private readonly ExternalApiOptions _options = options.Value;

    public async Task<string?> FindNearbyPlaceNameAsync(
        double latitude, double longitude, CancellationToken cancellationToken)
    {
        var name = await TryOverpassAsync(latitude, longitude, cancellationToken);
        if (name is not null)
        {
            return name;
        }

        // Overpass came back empty rather than erroring — still worth a line,
        // since "primary source empty, falling back" is a meaningfully
        // different case from "primary source failed" (already logged inside
        // TryOverpassAsync's catch) when reading these events as a sequence.
        logger.LogDebug(
            "Overpass found no candidate for {Lat},{Lon}; falling back to Nominatim", latitude, longitude);
        return await TryNominatimAsync(latitude, longitude, cancellationToken);
    }

    private async Task<string?> TryOverpassAsync(double lat, double lon, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var la = lat.ToString("R", CultureInfo.InvariantCulture);
            var lo = lon.ToString("R", CultureInfo.InvariantCulture);
            var query =
                $"[out:json][timeout:6];" +
                $"(node(around:50000,{la},{lo})[\"natural\"~\"^(peak|volcano|bay|cape|strait|glacier)$\"][\"name\"];" +
                $"way(around:50000,{la},{lo})[\"waterway\"~\"^(river|stream)$\"][\"name\"];" +
                $"node(around:50000,{la},{lo})[\"place\"~\"^(city|town|village|hamlet|isolated_dwelling|locality)$\"][\"name\"];" +
                $");out center 150;";

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(6));

            using var content = new FormUrlEncodedContent([new("data", query)]);
            var response = await httpClient.PostAsync($"{_options.Overpass}/api/interpreter", content, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "Overpass nearby-place lookup for {Lat},{Lon} returned {StatusCode} in {ElapsedMs}ms",
                    lat, lon, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<OverpassResponse>(timeoutCts.Token);

            string? bestName = null;
            var bestDistance = double.MaxValue;
            var candidateCount = 0;

            foreach (var element in body?.Elements ?? [])
            {
                var tags = element.Tags;
                var name = tags?.GetValueOrDefault("name:en")
                    ?? tags?.GetValueOrDefault("name:fr")
                    ?? tags?.GetValueOrDefault("name:de")
                    ?? tags?.GetValueOrDefault("name:es")
                    ?? LatinNameExtractor.Extract(tags?.GetValueOrDefault("name"));

                var elementLat = element.Lat ?? element.Center?.Lat;
                var elementLon = element.Lon ?? element.Center?.Lon;
                if (name is null || elementLat is null || elementLon is null)
                {
                    continue;
                }

                candidateCount++;
                var distance = GeoMath.HaversineKm(lat, lon, elementLat.Value, elementLon.Value);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestName = name;
                }
            }

            logger.LogDebug(
                "Overpass nearby-place lookup for {Lat},{Lon} evaluated {CandidateCount} candidates, " +
                "best {BestName} ({BestDistanceKm:F1}km) in {ElapsedMs}ms",
                lat, lon, candidateCount, bestName, bestDistance, stopwatch.ElapsedMilliseconds);
            return bestName;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Overpass nearby-place lookup failed for {Lat},{Lon} after {ElapsedMs}ms",
                lat, lon, stopwatch.ElapsedMilliseconds);
            return null;
        }
    }

    private async Task<string?> TryNominatimAsync(double lat, double lon, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            var la = lat.ToString("R", CultureInfo.InvariantCulture);
            var lo = lon.ToString("R", CultureInfo.InvariantCulture);
            var url = $"{_options.Nominatim}/reverse?format=jsonv2&lat={la}&lon={lo}&zoom=10&accept-language=en";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(_options.NominatimUserAgent);

            var response = await httpClient.SendAsync(request, timeoutCts.Token);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "Nominatim reverse-geocode for {Lat},{Lon} returned {StatusCode} in {ElapsedMs}ms",
                    lat, lon, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<NominatimReverseResponse>(timeoutCts.Token);
            var address = body?.Address ?? [];
            var candidate = address.GetValueOrDefault("city")
                ?? address.GetValueOrDefault("town")
                ?? address.GetValueOrDefault("village")
                ?? address.GetValueOrDefault("hamlet")
                ?? address.GetValueOrDefault("county");

            var name = LatinNameExtractor.Extract(candidate);
            logger.LogDebug(
                "Nominatim reverse-geocode for {Lat},{Lon} resolved to {Name} in {ElapsedMs}ms",
                lat, lon, name, stopwatch.ElapsedMilliseconds);
            return name;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            logger.LogWarning(ex,
                "Nominatim reverse-geocode fallback failed for {Lat},{Lon} after {ElapsedMs}ms",
                lat, lon, stopwatch.ElapsedMilliseconds);
            return null;
        }
    }
}
