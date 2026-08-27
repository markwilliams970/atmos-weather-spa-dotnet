using Atmos.Core.Services;

namespace Atmos.Core.Tests.Services;

public class AqiCategorizerTests
{
    [Theory]
    [InlineData(0, "Good", "#22c55e")]
    [InlineData(50, "Good", "#22c55e")]
    [InlineData(51, "Moderate", "#eab308")]
    [InlineData(100, "Moderate", "#eab308")]
    [InlineData(101, "Unhealthy · Sensitive", "#f97316")]
    [InlineData(150, "Unhealthy · Sensitive", "#f97316")]
    [InlineData(151, "Unhealthy", "#ef4444")]
    [InlineData(200, "Unhealthy", "#ef4444")]
    [InlineData(201, "Very Unhealthy", "#a855f7")]
    [InlineData(300, "Very Unhealthy", "#a855f7")]
    [InlineData(301, "Hazardous", "#dc2626")]
    [InlineData(500, "Hazardous", "#dc2626")]
    public void Categorize_matches_reference_thresholds(int aqi, string expectedCategory, string expectedColor)
    {
        var (category, color) = AqiCategorizer.Categorize(aqi);

        Assert.Equal(expectedCategory, category);
        Assert.Equal(expectedColor, color);
    }
}
