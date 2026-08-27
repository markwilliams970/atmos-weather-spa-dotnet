using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Atmos.Web.Tests.Integration;

/// <summary>Covers CLAUDE.md §18's "page routing" integration requirement across all four Razor Pages.</summary>
public sealed class PageRoutingTests(AtmosWebApplicationFactory factory) : IClassFixture<AtmosWebApplicationFactory>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/map")]
    [InlineData("/about")]
    [InlineData("/weather?zip=80002")]
    public async Task Known_pages_return_200(string path)
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_path_returns_404()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/this-page-does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Home_with_a_zip_query_string_redirects_to_the_weather_page()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/?zip=80002");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Weather?zip=80002", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Weather_page_without_a_location_redirects_home()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/weather");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        // Razor Pages maps the Index page to the app root, not a literal "/Index" URL.
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Weather_page_with_only_a_partial_lat_lon_label_set_redirects_home()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/weather?lat=39.8&lon=-105.08");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}
