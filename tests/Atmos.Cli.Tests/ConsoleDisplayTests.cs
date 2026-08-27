using Atmos.Cli;
using Atmos.Core.Models;

namespace Atmos.Cli.Tests;

/// <summary>
/// Golden-file regression test. The fixture file was captured directly from
/// this app's actual console output for ZIP 80002, cross-checked byte-for-
/// byte against the reference TypeScript CLI (weather.ts) run against the
/// same live data — including the box-alignment quirk on lines containing an
/// emoji, which is the *original* app's own behavior (JS/C# both count
/// UTF-16 code units, not terminal display width, so an emoji throws the
/// padding math off in both languages identically) and must NOT be "fixed"
/// during any future change to ConsoleDisplay.
/// </summary>
public class ConsoleDisplayTests
{
    private static readonly Location ArvadaCo = new("Arvada", "CO", 39.7945, -105.0984);

    private static readonly WeatherForecast Forecast = new(
        Location: "Arvada, CO",
        Zip: "80002",
        Latitude: 39.7945,
        Longitude: -105.0984,
        TempF: 92,
        TempC: 33,
        FeelsLikeF: 89,
        FeelsLikeC: 32,
        Humidity: 13,
        WindMph: 8,
        WindKmh: 13,
        WindDir: "ENE",
        WindDeg: 63,
        PrecipIn: 0,
        PrecipMm: 0,
        UvIndex: 7.15,
        Condition: "Mainly Clear",
        ConditionEmoji: "🌤️",
        Sunrise: "6:24 AM",
        Sunset: "7:39 PM",
        SunriseMin: 384,
        SunsetMin: 1179,
        IsDay: true,
        TodayHighF: 92,
        TodayHighC: 33,
        TodayLowF: 60,
        TodayLowC: 16,
        Hourly: [],
        Daily: [],
        ElevationMeters: null);

    [Fact]
    public void Show_matches_the_reference_cli_byte_for_byte()
    {
        var expected = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "arvada-co-expected-output.txt"));

        var originalOut = Console.Out;
        var writer = new StringWriter { NewLine = "\n" };
        try
        {
            Console.SetOut(writer);
            ConsoleDisplay.Show(ArvadaCo, Forecast);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Equal(expected, writer.ToString());
    }
}
