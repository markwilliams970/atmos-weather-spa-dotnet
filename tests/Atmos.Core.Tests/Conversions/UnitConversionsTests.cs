using Atmos.Core.Conversions;

namespace Atmos.Core.Tests.Conversions;

public class UnitConversionsTests
{
    [Theory]
    [InlineData(0, 32)]
    [InlineData(100, 212)]
    [InlineData(-40, -40)]
    [InlineData(20, 68)]
    public void CelsiusToFahrenheit_matches_reference_rounding(double celsius, int expectedFahrenheit)
    {
        Assert.Equal(expectedFahrenheit, UnitConversions.CelsiusToFahrenheit(celsius));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 62)]
    [InlineData(10, 6)]
    public void KmhToMph_matches_reference_rounding(double kmh, int expectedMph)
    {
        Assert.Equal(expectedMph, UnitConversions.KmhToMph(kmh));
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(25.4, 1.0)]
    [InlineData(10, 0.39)]
    public void MillimetersToInches_rounds_to_two_decimals(double mm, double expectedIn)
    {
        Assert.Equal(expectedIn, UnitConversions.MillimetersToInches(mm), 2);
    }

    [Fact]
    public void MetersToFeet_is_unrounded_conversion_factor()
    {
        // Rounding for display is a presentation concern applied at the call
        // site (e.g. Math.round(elevationM * 3.28084) client-side) — the
        // conversion itself stays a precise double, matching the original's
        // "elevationM * 3.28084" expression before any Math.round is applied.
        Assert.Equal(3280.84, UnitConversions.MetersToFeet(1000), 2);
    }

    [Theory]
    [InlineData(0, "N")]
    [InlineData(90, "E")]
    [InlineData(180, "S")]
    [InlineData(270, "W")]
    [InlineData(360, "N")]
    [InlineData(11, "N")]     // rounds down into N
    [InlineData(12, "NNE")]   // rounds up into NNE
    public void DegreesToCompass_matches_reference_16_point_table(double degrees, string expectedDirection)
    {
        Assert.Equal(expectedDirection, UnitConversions.DegreesToCompass(degrees));
    }

    [Theory]
    [InlineData(2.5, 3)]   // JS Math.round(2.5) === 3
    [InlineData(-2.5, -2)] // JS Math.round(-2.5) === -2 (rounds toward +Infinity, not away from zero)
    [InlineData(2.4, 2)]
    [InlineData(2.6, 3)]
    public void JsRound_matches_javascript_half_up_semantics_not_bankers_rounding(double value, double expected)
    {
        Assert.Equal(expected, UnitConversions.JsRound(value));
    }
}
