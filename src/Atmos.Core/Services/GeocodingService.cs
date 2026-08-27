using System.Diagnostics;
using System.Net.Http.Json;
using Atmos.Core.Configuration;
using Atmos.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atmos.Core.Services;

public sealed class GeocodingService(
    HttpClient httpClient,
    IOptions<ExternalApiOptions> options,
    ILogger<GeocodingService> logger) : IGeocodingService
{
    private readonly ExternalApiOptions _options = options.Value;

    public async Task<Location?> LookupZipAsync(string zip, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await httpClient.GetAsync(
                $"{_options.Zippopotam}/us/{zip}", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "ZIP lookup for {Zip} returned {StatusCode} in {ElapsedMs}ms",
                    zip, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<ZippopotamResponse>(cancellationToken);
            var place = body?.Places?.FirstOrDefault();
            if (place is null
                || place.PlaceName is null
                || place.StateAbbreviation is null
                || !double.TryParse(place.Latitude, out var lat)
                || !double.TryParse(place.Longitude, out var lon))
            {
                logger.LogInformation(
                    "ZIP lookup for {Zip} returned no usable place in {ElapsedMs}ms",
                    zip, stopwatch.ElapsedMilliseconds);
                return null;
            }

            logger.LogDebug(
                "ZIP lookup resolved {Zip} -> {City}, {State} in {ElapsedMs}ms",
                zip, place.PlaceName, place.StateAbbreviation, stopwatch.ElapsedMilliseconds);
            return new Location(place.PlaceName, place.StateAbbreviation, lat, lon);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex,
                "ZIP lookup failed for {Zip} after {ElapsedMs}ms", zip, stopwatch.ElapsedMilliseconds);
            return null;
        }
    }

    public async Task<IReadOnlyList<GeocodeResult>> SearchCityAsync(
        string query, int count, CancellationToken cancellationToken)
    {
        if (query.Length < 2)
        {
            return [];
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var url = $"{_options.OpenMeteoGeocoding}/v1/search" +
                      $"?name={Uri.EscapeDataString(query)}&count={count}&language=en&format=json";

            var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "City search for {Query} returned {StatusCode} in {ElapsedMs}ms",
                    query, (int)response.StatusCode, stopwatch.ElapsedMilliseconds);
                return [];
            }

            var body = await response.Content.ReadFromJsonAsync<OpenMeteoGeocodingResponse>(cancellationToken);
            var results = body?.Results?
                .Select(r => new GeocodeResult(r.Name, r.Admin1, r.CountryCode, r.Latitude, r.Longitude))
                .ToList()
                ?? [];

            logger.LogDebug(
                "City search for {Query} returned {ResultCount} results in {ElapsedMs}ms",
                query, results.Count, stopwatch.ElapsedMilliseconds);
            return results;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Autocomplete is non-fatal by design — matches the reference app's
            // catch -> {results:[]} behavior (weather-server.ts handleGeocode).
            logger.LogWarning(ex,
                "City search failed for {Query} after {ElapsedMs}ms", query, stopwatch.ElapsedMilliseconds);
            return [];
        }
    }
}
