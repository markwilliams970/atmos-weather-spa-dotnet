using System.Net;

namespace Atmos.Web.Tests.Integration;

/// <summary>Covers CLAUDE.md §18's "application startup" and "database initialization" integration requirements.</summary>
public sealed class StartupTests(AtmosWebApplicationFactory factory) : IClassFixture<AtmosWebApplicationFactory>
{
    [Fact]
    public async Task App_starts_and_the_database_health_check_reports_healthy()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
