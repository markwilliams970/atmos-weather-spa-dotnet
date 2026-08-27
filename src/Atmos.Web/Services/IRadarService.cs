using Atmos.Web.Models;

namespace Atmos.Web.Services;

public interface IRadarService
{
    /// <summary>
    /// Returns null if RainViewer's frame list can't be retrieved — the radar
    /// card then falls back to basemap-only tiles with no overlay, matching the
    /// reference app's silent-degradation behavior for this card.
    /// </summary>
    Task<RadarFrame?> GetLatestFrameAsync(CancellationToken cancellationToken);
}
