using Atmos.Web.Services;

namespace Atmos.Web.Endpoints;

/// <summary>New endpoint — the RainViewer frame lookup moved server-side per Phase B decision #3.</summary>
public static class RadarEndpoints
{
    public static void MapRadarEndpoints(this WebApplication app)
    {
        app.MapGet("/api/radar/frame", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(IRadarService radar, CancellationToken cancellationToken)
    {
        var frame = await radar.GetLatestFrameAsync(cancellationToken);
        return Results.Ok(frame);
    }
}
