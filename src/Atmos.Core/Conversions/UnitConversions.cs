namespace Atmos.Core.Conversions;

/// <summary>
/// Pure unit-conversion helpers, ported verbatim (rounding included) from the
/// reference TypeScript app so the .NET forecast output matches it exactly.
/// </summary>
public static class UnitConversions
{
    private static readonly string[] CompassDirections =
    [
        "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE",
        "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW"
    ];

    public static string DegreesToCompass(double degrees)
    {
        var index = (int)JsRound(degrees / 22.5) % 16;
        if (index < 0)
        {
            index += 16;
        }

        return CompassDirections[index];
    }

    public static int CelsiusToFahrenheit(double celsius) => (int)JsRound(celsius * 9 / 5 + 32);

    public static int KmhToMph(double kmh) => (int)JsRound(kmh * 0.621371);

    public static double MillimetersToInches(double mm) => JsRound(mm * 0.0393701 * 100) / 100;

    public static double MetersToFeet(double meters) => meters * 3.28084;

    /// <summary>
    /// Rounds like JavaScript's Math.round: half-way values always round toward
    /// positive infinity (so -2.5 rounds to -2, not -3), unlike .NET's default
    /// banker's rounding. Needed for byte-for-byte parity with the reference app.
    /// </summary>
    public static double JsRound(double value) => Math.Floor(value + 0.5);
}
