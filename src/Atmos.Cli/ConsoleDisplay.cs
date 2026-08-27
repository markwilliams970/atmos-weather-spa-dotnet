using Atmos.Core.Models;

namespace Atmos.Cli;

/// <summary>
/// Ported from weather.ts's display() (weather.ts:155-181). Kept local to
/// Atmos.Cli rather than shared with Atmos.Core — this is presentation, not
/// domain logic, and the web app has no equivalent "print a box to a
/// terminal" concept for it to share (CLAUDE.md §35).
/// </summary>
internal static class ConsoleDisplay
{
    public static void Show(Location location, WeatherForecast w)
    {
        var title = $" {w.ConditionEmoji}  {location.City}, {location.State} ";
        var width = Math.Max(title.Length + 4, 30);
        var horizontalRule = new string('─', width - 2);

        string Pad(string s)
        {
            var spaces = new string(' ', Math.Max(0, width - 2 - s.Length - 2));
            return $"│ {s}{spaces} │";
        }

        Console.WriteLine();
        Console.WriteLine($"┌{horizontalRule}┐");
        Console.WriteLine(Pad(title));
        Console.WriteLine($"├{horizontalRule}┤");
        Console.WriteLine(Pad($"  🌡️  Temperature   : {w.TempF}°F  (feels like {w.FeelsLikeF}°F)"));
        Console.WriteLine(Pad($"  💧  Humidity      : {w.Humidity}%"));
        Console.WriteLine(Pad($"  💨  Wind          : {w.WindMph} mph {w.WindDir}"));
        Console.WriteLine(Pad($"  🌧️  Precipitation  : {w.PrecipIn} in"));
        Console.WriteLine(Pad($"  ☀️  UV Index       : {w.UvIndex}"));
        Console.WriteLine(Pad($"  🌅  Sunrise        : {w.Sunrise}"));
        Console.WriteLine(Pad($"  🌇  Sunset         : {w.Sunset}"));
        Console.WriteLine(Pad($"  📍  Coordinates   : {location.Latitude.ToString("F4")}, {location.Longitude.ToString("F4")}"));
        Console.WriteLine(Pad($"  🗺️  Map            : https://www.openstreetmap.org/?mlat={location.Latitude}&mlon={location.Longitude}#map=14/{location.Latitude}/{location.Longitude}"));
        Console.WriteLine($"├{horizontalRule}┤");
        Console.WriteLine(Pad($"  {w.ConditionEmoji}  {w.Condition}"));
        Console.WriteLine($"└{horizontalRule}┘");
        Console.WriteLine();
    }
}
