using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Atmos.Web.Models;

namespace Atmos.Web.Tests.Integration;

/// <summary>
/// Light routing/binding coverage for the remaining /api endpoints not
/// exercised by WeatherEndpointTests or RecentSearchTests — their external
/// call logic is already covered by the fixture-based tests in Services/.
/// </summary>
public sealed class OtherEndpointTests(AtmosWebApplicationFactory factory) : IClassFixture<AtmosWebApplicationFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Geocode_returns_matches_for_a_known_city()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/geocode?q=Denver");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<GeocodeResultsDto>(Json);
        Assert.Single(body!.Results);
    }

    [Fact]
    public async Task Geocode_with_no_query_returns_an_empty_result_set_rather_than_an_error()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/geocode");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<GeocodeResultsDto>(Json);
        Assert.Empty(body!.Results);
    }

    [Fact]
    public async Task Air_quality_returns_data_for_valid_coordinates()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/air-quality?lat=39.8&lon=-105.08");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AirQuality>(Json);
        Assert.Equal(42, body?.UsAqi);
    }

    [Fact]
    public async Task Air_quality_without_coordinates_returns_400()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/air-quality");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Elevation_returns_a_value_for_valid_coordinates()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/elevation?lat=39.8&lon=-105.08");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ElevationDto>(Json);
        Assert.Equal(1655.0, body?.Elevation);
    }

    [Fact]
    public async Task Elevation_without_coordinates_returns_400()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/elevation");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Nearby_place_returns_a_name_for_valid_coordinates()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/nearby-place?lat=39.8&lon=-105.08");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<NearbyPlaceDto>(Json);
        Assert.Equal("Table Mountain", body?.Name);
    }

    [Fact]
    public async Task Radar_frame_returns_the_latest_frame_metadata()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/radar/frame");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RadarFrameDto>(Json);
        Assert.False(string.IsNullOrEmpty(body?.Host));
    }

    private sealed record GeocodeResultsDto(List<object> Results);
    private sealed record ElevationDto(double? Elevation);
    private sealed record NearbyPlaceDto(string? Name);
    private sealed record RadarFrameDto(string Host, string Path);
}
