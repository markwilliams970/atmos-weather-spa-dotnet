namespace Atmos.Web.Models;

/// <summary>
/// Enough for the browser to build RainViewer tile URLs itself
/// ({Host}{Path}/256/{z}/{x}/{y}/4/0_0.png) — the tile math and rendering stay
/// entirely client-side, exactly as in the reference app; only the frame
/// metadata lookup moved server-side (Phase B decision #3).
/// </summary>
public sealed record RadarFrame(string Host, string Path, DateTimeOffset FrameTimeUtc);
