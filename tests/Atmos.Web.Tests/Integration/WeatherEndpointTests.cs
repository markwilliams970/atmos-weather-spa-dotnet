using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Atmos.Web.Models;
using Atmos.Web.Tests.Integration.Fakes;

namespace Atmos.Web.Tests.Integration;

/// <summary>Covers CLAUDE.md §18's "weather endpoints" and "invalid input" integration requirements.</summary>
public sealed class WeatherEndpointTests(AtmosWebApplicationFactory factory) : IClassFixture<AtmosWebApplicationFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Known_zip_returns_a_forecast()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/weather?zip=80002");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forecast = await response.Content.ReadFromJsonAsync<WeatherForecastDto>(Json);
        Assert.Equal("Arvada, CO", forecast?.Location);
        Assert.Equal("80002", forecast?.Zip);
    }

    [Fact]
    public async Task Unknown_zip_returns_400()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/weather?zip=00000");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(Json);
        Assert.Contains("not found", error?.Error);
    }

    [Theory]
    [InlineData("/api/weather?zip=abc")]
    [InlineData("/api/weather?zip=123")]
    public async Task Malformed_zip_returns_400(string path)
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task No_location_provided_returns_400()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/weather");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Lat_lon_label_returns_a_forecast()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/weather?lat=39.8&lon=-105.08&label=My%20Place");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var forecast = await response.Content.ReadFromJsonAsync<WeatherForecastDto>(Json);
        Assert.Equal("My Place", forecast?.Location);
    }

    [Theory]
    [InlineData("lat=999&lon=-105.08&label=X")]
    [InlineData("lat=39.8&lon=-999&label=X")]
    public async Task Out_of_range_coordinates_return_400(string query)
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/weather?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Overlong_label_returns_400()
    {
        var client = factory.CreateClient();
        var label = new string('x', 201);

        var response = await client.GetAsync($"/api/weather?lat=39.8&lon=-105.08&label={label}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upstream_weather_failure_returns_502_without_leaking_the_raw_exception()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/weather?lat=39.8&lon=-105.08&label={FakeWeatherService.FailingCity}");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(Json);
        Assert.Equal("Weather data is temporarily unavailable.", error?.Error);
    }

    private sealed record WeatherForecastDto(string Location, string Zip);
}
