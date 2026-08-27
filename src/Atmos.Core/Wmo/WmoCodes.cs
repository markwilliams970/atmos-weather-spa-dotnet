namespace Atmos.Core.Wmo;

/// <summary>
/// WMO weather-code lookup, ported from the reference TypeScript app's WMO table
/// (weather-server.ts / weather.ts). Shared by Atmos.Web and Atmos.Cli so the two
/// apps can never drift on condition labels/emoji.
/// </summary>
public static class WmoCodes
{
    private static readonly WmoCondition Unknown = new("Unknown", "❓");

    private static readonly IReadOnlyDictionary<int, WmoCondition> Table = new Dictionary<int, WmoCondition>
    {
        [0] = new("Clear Sky", "☀️"),
        [1] = new("Mainly Clear", "🌤️"),
        [2] = new("Partly Cloudy", "⛅"),
        [3] = new("Overcast", "☁️"),
        [45] = new("Foggy", "🌫️"),
        [48] = new("Icy Fog", "🌫️"),
        [51] = new("Light Drizzle", "🌦️"),
        [53] = new("Drizzle", "🌦️"),
        [55] = new("Heavy Drizzle", "🌧️"),
        [61] = new("Light Rain", "🌧️"),
        [63] = new("Rain", "🌧️"),
        [65] = new("Heavy Rain", "🌧️"),
        [71] = new("Light Snow", "🌨️"),
        [73] = new("Snow", "❄️"),
        [75] = new("Heavy Snow", "❄️"),
        [77] = new("Snow Grains", "🌨️"),
        [80] = new("Rain Showers", "🌦️"),
        [81] = new("Rain Showers", "🌧️"),
        [82] = new("Violent Showers", "⛈️"),
        [85] = new("Snow Showers", "🌨️"),
        [86] = new("Heavy Snow Showers", "❄️"),
        [95] = new("Thunderstorm", "⛈️"),
        [96] = new("Thunderstorm + Hail", "⛈️"),
        [99] = new("Thunderstorm + Hail", "⛈️"),
    };

    public static WmoCondition Lookup(int code) => Table.GetValueOrDefault(code, Unknown);
}
