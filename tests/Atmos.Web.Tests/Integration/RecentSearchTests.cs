using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Atmos.Web.Models;

namespace Atmos.Web.Tests.Integration;

/// <summary>
/// Covers CLAUDE.md §18's "recent-search persistence" and "unit preference
/// persistence" integration requirements, end to end through the real HTTP
/// pipeline (session cookie -&gt; endpoint -&gt; EF Core -&gt; back out through
/// /api/recent), plus the same-origin check guarding the one mutating
/// endpoint (Phase B §15/§20).
/// </summary>
public sealed class RecentSearchTests(AtmosWebApplicationFactory factory) : IClassFixture<AtmosWebApplicationFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_weather_search_is_recorded_and_retrievable_via_recent()
    {
        var client = factory.CreateClient();
        await client.GetAsync("/api/weather?zip=80002");

        var response = await client.GetAsync("/api/recent");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var recent = await response.Content.ReadFromJsonAsync<List<RecentSearchResponse>>(Json);
        var entry = Assert.Single(recent!);
        Assert.Equal("Arvada, CO", entry.Label);
        Assert.Equal("imperial", entry.Units);
    }

    [Fact]
    public async Task Recent_searches_are_scoped_per_session_not_shared_globally()
    {
        var clientA = factory.CreateClient();
        var clientB = factory.CreateClient();
        await clientA.GetAsync("/api/weather?zip=80002");

        var recentForB = await (await clientB.GetAsync("/api/recent")).Content.ReadFromJsonAsync<List<RecentSearchResponse>>(Json);

        Assert.Empty(recentForB!);
    }

    [Fact]
    public async Task Updating_units_for_an_existing_label_is_reflected_in_recent()
    {
        var client = factory.CreateClient();
        await client.GetAsync("/api/weather?zip=80002");

        var putResponse = await PutUnits(client, "Arvada, CO", "metric", sameOrigin: true);

        Assert.Equal(HttpStatusCode.NoContent, putResponse.StatusCode);
        var recent = await (await client.GetAsync("/api/recent")).Content.ReadFromJsonAsync<List<RecentSearchResponse>>(Json);
        Assert.Equal("metric", Assert.Single(recent!).Units);
    }

    [Fact]
    public async Task Updating_units_without_a_matching_origin_header_is_rejected()
    {
        var client = factory.CreateClient();
        await client.GetAsync("/api/weather?zip=80002");

        var response = await PutUnits(client, "Arvada, CO", "metric", sameOrigin: false);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Updating_units_with_an_empty_label_returns_400()
    {
        var client = factory.CreateClient();

        var response = await PutUnits(client, "", "metric", sameOrigin: true);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Updating_units_for_an_unknown_label_is_a_silent_no_op()
    {
        var client = factory.CreateClient();

        var response = await PutUnits(client, "Nowhere, ZZ", "metric", sameOrigin: true);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> PutUnits(HttpClient client, string label, string units, bool sameOrigin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/recent/units")
        {
            Content = JsonContent.Create(new RecentSearchUnitsRequest(label, units)),
        };
        if (sameOrigin)
        {
            request.Headers.Add("Origin", client.BaseAddress!.GetLeftPart(UriPartial.Authority));
        }

        return await client.SendAsync(request);
    }
}
