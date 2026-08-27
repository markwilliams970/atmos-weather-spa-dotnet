using System.Net;
using Atmos.Web.Services;
using Atmos.Web.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Atmos.Web.Tests.Services;

public class NearbyPlaceServiceTests
{
    private static bool IsOverpass(HttpRequestMessage req) => req.RequestUri!.Host.Contains("overpass");
    private static bool IsNominatim(HttpRequestMessage req) => req.RequestUri!.Host.Contains("nominatim");

    [Fact]
    public async Task FindNearbyPlaceNameAsync_picks_the_nearest_overpass_candidate_by_haversine_distance()
    {
        // Two candidates near a point at (40.0, -105.0): a farther one listed
        // first, a nearer one listed second — the nearer one must win.
        const string overpassJson = """
            {
              "elements": [
                { "lat": 41.0, "lon": -106.0, "tags": { "name": "Farmountain" } },
                { "lat": 40.01, "lon": -105.01, "tags": { "name": "Nearpeak" } }
              ]
            }
            """;
        var client = FakeHttpMessageHandler.CreateSequencedClient(
            (IsOverpass, new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(overpassJson) }));
        var service = new NearbyPlaceService(client, Options.Create(TestExternalApiOptions.Create()), NullLogger<NearbyPlaceService>.Instance);

        var name = await service.FindNearbyPlaceNameAsync(40.0, -105.0, CancellationToken.None);

        Assert.Equal("Nearpeak", name);
    }

    [Fact]
    public async Task FindNearbyPlaceNameAsync_prefers_name_en_tag_over_raw_name()
    {
        const string overpassJson = """
            {
              "elements": [
                { "lat": 40.01, "lon": -105.01, "tags": { "name": "拉萨", "name:en": "Lhasa" } }
              ]
            }
            """;
        var client = FakeHttpMessageHandler.CreateSequencedClient(
            (IsOverpass, new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(overpassJson) }));
        var service = new NearbyPlaceService(client, Options.Create(TestExternalApiOptions.Create()), NullLogger<NearbyPlaceService>.Instance);

        var name = await service.FindNearbyPlaceNameAsync(40.0, -105.0, CancellationToken.None);

        Assert.Equal("Lhasa", name);
    }

    [Fact]
    public async Task FindNearbyPlaceNameAsync_uses_center_for_way_elements_without_lat_lon()
    {
        const string overpassJson = """
            { "elements": [ { "center": { "lat": 40.01, "lon": -105.01 }, "tags": { "name": "River Bend" } } ] }
            """;
        var client = FakeHttpMessageHandler.CreateSequencedClient(
            (IsOverpass, new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(overpassJson) }));
        var service = new NearbyPlaceService(client, Options.Create(TestExternalApiOptions.Create()), NullLogger<NearbyPlaceService>.Instance);

        var name = await service.FindNearbyPlaceNameAsync(40.0, -105.0, CancellationToken.None);

        Assert.Equal("River Bend", name);
    }

    [Fact]
    public async Task FindNearbyPlaceNameAsync_falls_back_to_nominatim_when_overpass_finds_nothing()
    {
        const string nominatimJson = """{ "address": { "town": "Golden", "county": "Jefferson County" } }""";
        var client = FakeHttpMessageHandler.CreateSequencedClient(
            (IsOverpass, new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{ "elements": [] }""") }),
            (IsNominatim, new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(nominatimJson) }));
        var service = new NearbyPlaceService(client, Options.Create(TestExternalApiOptions.Create()), NullLogger<NearbyPlaceService>.Instance);

        var name = await service.FindNearbyPlaceNameAsync(40.0, -105.0, CancellationToken.None);

        Assert.Equal("Golden", name); // "city" and "town" precede "county" in fallback order, town wins here since city absent
    }

    [Fact]
    public async Task FindNearbyPlaceNameAsync_returns_null_when_both_sources_fail()
    {
        var client = FakeHttpMessageHandler.CreateClient(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var service = new NearbyPlaceService(client, Options.Create(TestExternalApiOptions.Create()), NullLogger<NearbyPlaceService>.Instance);

        var name = await service.FindNearbyPlaceNameAsync(40.0, -105.0, CancellationToken.None);

        Assert.Null(name);
    }
}
