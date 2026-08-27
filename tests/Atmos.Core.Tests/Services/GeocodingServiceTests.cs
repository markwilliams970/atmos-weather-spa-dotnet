using Atmos.Core.Configuration;
using Atmos.Core.Services;
using Atmos.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Atmos.Core.Tests.Services;

public class GeocodingServiceTests
{
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
    public async Task LookupZipAsync_maps_zippopotam_response_to_location()
    {
        const string json = """
            {
              "places": [
                { "place name": "Arvada", "state abbreviation": "CO", "latitude": "39.8283", "longitude": "-105.0844" }
              ]
            }
            """;
        var client = FakeHttpMessageHandler.CreateJsonClient(json);
        var service = new GeocodingService(client, Options.Create(TestOptions), NullLogger<GeocodingService>.Instance);

        var location = await service.LookupZipAsync("80002", CancellationToken.None);

        Assert.NotNull(location);
        Assert.Equal("Arvada", location.City);
        Assert.Equal("CO", location.State);
        Assert.Equal(39.8283, location.Latitude);
        Assert.Equal(-105.0844, location.Longitude);
    }

    [Fact]
    public async Task LookupZipAsync_returns_null_for_unknown_zip()
    {
        var client = FakeHttpMessageHandler.CreateClient(
            _ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        var service = new GeocodingService(client, Options.Create(TestOptions), NullLogger<GeocodingService>.Instance);

        var location = await service.LookupZipAsync("00000", CancellationToken.None);

        Assert.Null(location);
    }

    [Fact]
    public async Task SearchCityAsync_maps_open_meteo_results()
    {
        const string json = """
            {
              "results": [
                { "name": "Denver", "admin1": "Colorado", "country_code": "US", "latitude": 39.7392, "longitude": -104.9847 }
              ]
            }
            """;
        var client = FakeHttpMessageHandler.CreateJsonClient(json);
        var service = new GeocodingService(client, Options.Create(TestOptions), NullLogger<GeocodingService>.Instance);

        var results = await service.SearchCityAsync("Denver", 5, CancellationToken.None);

        var result = Assert.Single(results);
        Assert.Equal("Denver", result.Name);
        Assert.Equal("Colorado", result.Admin1);
        Assert.Equal("US", result.CountryCode);
    }

    [Fact]
    public async Task SearchCityAsync_fails_gracefully_to_empty_list_never_throws()
    {
        var client = FakeHttpMessageHandler.CreateClient(
            _ => throw new HttpRequestException("simulated network failure"));
        var service = new GeocodingService(client, Options.Create(TestOptions), NullLogger<GeocodingService>.Instance);

        var results = await service.SearchCityAsync("Denver", 5, CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchCityAsync_short_query_short_circuits_without_a_request()
    {
        var called = false;
        var client = FakeHttpMessageHandler.CreateClient(_ =>
        {
            called = true;
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        });
        var service = new GeocodingService(client, Options.Create(TestOptions), NullLogger<GeocodingService>.Instance);

        var results = await service.SearchCityAsync("D", 5, CancellationToken.None);

        Assert.Empty(results);
        Assert.False(called);
    }
}
