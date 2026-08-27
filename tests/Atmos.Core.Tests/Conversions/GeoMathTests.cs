using Atmos.Core.Conversions;

namespace Atmos.Core.Tests.Conversions;

public class GeoMathTests
{
    [Fact]
    public void HaversineKm_returns_zero_for_identical_points()
    {
        Assert.Equal(0, GeoMath.HaversineKm(40.0, -105.0, 40.0, -105.0), 6);
    }

    [Fact]
    public void HaversineKm_matches_known_distance_denver_to_boulder()
    {
        // Denver, CO -> Boulder, CO is ~40km great-circle.
        var km = GeoMath.HaversineKm(39.7392, -104.9903, 40.0150, -105.2705);

        Assert.InRange(km, 35, 45);
    }
}
