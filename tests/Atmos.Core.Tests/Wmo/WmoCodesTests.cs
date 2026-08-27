using Atmos.Core.Wmo;

namespace Atmos.Core.Tests.Wmo;

public class WmoCodesTests
{
    [Theory]
    [InlineData(0, "Clear Sky", "☀️")]
    [InlineData(63, "Rain", "🌧️")]
    [InlineData(95, "Thunderstorm", "⛈️")]
    [InlineData(99, "Thunderstorm + Hail", "⛈️")]
    public void Lookup_returns_known_condition(int code, string label, string emoji)
    {
        var condition = WmoCodes.Lookup(code);

        Assert.Equal(label, condition.Label);
        Assert.Equal(emoji, condition.Emoji);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(1000)]
    public void Lookup_falls_back_to_unknown_for_unmapped_code(int code)
    {
        var condition = WmoCodes.Lookup(code);

        Assert.Equal("Unknown", condition.Label);
        Assert.Equal("❓", condition.Emoji);
    }
}
