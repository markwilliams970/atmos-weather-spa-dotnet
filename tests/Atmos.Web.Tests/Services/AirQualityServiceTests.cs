using Atmos.Web.Services;
using Atmos.Web.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Atmos.Web.Tests.Services;

public class AirQualityServiceTests
{
    [Fact]
    public async Task GetAirQualityAsync_maps_and_categorizes_from_the_single_source_of_truth()
    {
        const string json = """
            { "current": { "us_aqi": 42, "pm10": 12.34, "pm2_5": 8.76, "ozone": 30.2, "nitrogen_dioxide": 15.9 } }
            """;
        var client = FakeHttpMessageHandler.CreateJsonClient(json);
        var service = new AirQualityService(client, Options.Create(TestExternalApiOptions.Create()), NullLogger<AirQualityService>.Instance);

        var aq = await service.GetAirQualityAsync(40.0, -105.0, CancellationToken.None);

        Assert.Equal(42, aq.UsAqi);
        Assert.Equal(8.8, aq.Pm25);
        Assert.Equal(12.3, aq.Pm10);
        Assert.Equal("Good", aq.Category);
        Assert.Equal("#22c55e", aq.Color);
    }

    [Fact]
    public async Task GetAirQualityAsync_throws_unavailable_exception_on_failure_rather_than_a_raw_error()
    {
        var client = FakeHttpMessageHandler.CreateClient(
            _ => new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
        var service = new AirQualityService(client, Options.Create(TestExternalApiOptions.Create()), NullLogger<AirQualityService>.Instance);

        await Assert.ThrowsAsync<AirQualityUnavailableException>(() =>
            service.GetAirQualityAsync(40.0, -105.0, CancellationToken.None));
    }
}
