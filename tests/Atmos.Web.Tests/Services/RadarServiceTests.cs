using Atmos.Web.Services;
using Atmos.Web.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Atmos.Web.Tests.Services;

/// <summary>
/// The reference app has a documented production incident (Claude.md:188-197):
/// an earlier version built radar tile URLs from RainViewer's raw per-frame
/// "time" timestamp, which silently started 410'ing when RainViewer changed
/// its API to return an opaque "host" + "path" pair instead. These tests exist
/// specifically to make that regression impossible to reintroduce silently.
/// </summary>
public class RadarServiceTests
{
    [Fact]
    public async Task GetLatestFrameAsync_uses_host_plus_path_never_the_raw_timestamp()
    {
        const string json = """
            {
              "host": "https://tilecache.rainviewer.com",
              "radar": {
                "past": [
                  { "time": 1700000000, "path": "/v2/radar/aaa111" },
                  { "time": 1700000600, "path": "/v2/radar/023f41cb7314" }
                ]
              }
            }
            """;
        var client = FakeHttpMessageHandler.CreateJsonClient(json);
        var service = new RadarService(client, Options.Create(TestExternalApiOptions.Create()), NullLogger<RadarService>.Instance);

        var frame = await service.GetLatestFrameAsync(CancellationToken.None);

        Assert.NotNull(frame);
        Assert.Equal("https://tilecache.rainviewer.com", frame.Host);
        Assert.Equal("/v2/radar/023f41cb7314", frame.Path); // the LAST (most recent) frame, not the first
    }

    [Fact]
    public async Task GetLatestFrameAsync_falls_back_to_default_host_when_missing()
    {
        const string json = """{ "radar": { "past": [ { "time": 1700000000, "path": "/v2/radar/xyz" } ] } } """;
        var client = FakeHttpMessageHandler.CreateJsonClient(json);
        var service = new RadarService(client, Options.Create(TestExternalApiOptions.Create()), NullLogger<RadarService>.Instance);

        var frame = await service.GetLatestFrameAsync(CancellationToken.None);

        Assert.Equal("https://tilecache.rainviewer.com", frame!.Host);
    }

    [Fact]
    public async Task GetLatestFrameAsync_returns_null_when_no_frames_or_on_failure()
    {
        var emptyClient = FakeHttpMessageHandler.CreateJsonClient("""{ "host": "x", "radar": { "past": [] } }""");
        var emptyService = new RadarService(emptyClient, Options.Create(TestExternalApiOptions.Create()), NullLogger<RadarService>.Instance);
        Assert.Null(await emptyService.GetLatestFrameAsync(CancellationToken.None));

        var failingClient = FakeHttpMessageHandler.CreateClient(_ => throw new HttpRequestException("down"));
        var failingService = new RadarService(failingClient, Options.Create(TestExternalApiOptions.Create()), NullLogger<RadarService>.Instance);
        Assert.Null(await failingService.GetLatestFrameAsync(CancellationToken.None));
    }
}
