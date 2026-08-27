using Atmos.Web.Services;
using Atmos.Web.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Atmos.Web.Tests.Services;

public class ElevationServiceTests
{
    [Fact]
    public async Task GetElevationAsync_returns_first_value()
    {
        var client = FakeHttpMessageHandler.CreateJsonClient("""{ "elevation": [1655.0] }""");
        var service = new ElevationService(client, Options.Create(TestExternalApiOptions.Create()), NullLogger<ElevationService>.Instance);

        var elevation = await service.GetElevationAsync(40.0, -105.0, CancellationToken.None);

        Assert.Equal(1655.0, elevation);
    }

    [Fact]
    public async Task GetElevationAsync_returns_null_on_failure_rather_than_throwing()
    {
        var client = FakeHttpMessageHandler.CreateClient(_ => throw new HttpRequestException("down"));
        var service = new ElevationService(client, Options.Create(TestExternalApiOptions.Create()), NullLogger<ElevationService>.Instance);

        Assert.Null(await service.GetElevationAsync(40.0, -105.0, CancellationToken.None));
    }
}
