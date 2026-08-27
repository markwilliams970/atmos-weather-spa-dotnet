namespace Atmos.Core.Services;

/// <summary>
/// US AQI category/color thresholds, ported from fetchAirQuality
/// (weather-server.ts:284-291). The reference app duplicated this in the browser
/// (aqCat()) and the client there recomputed it instead of trusting the server's
/// own category/color fields — this is now the single source of truth, consumed
/// by Atmos.Web's IAirQualityService; the client only renders what it's given.
/// </summary>
public static class AqiCategorizer
{
    public static (string Category, string Color) Categorize(int aqi) => aqi switch
    {
        <= 50 => ("Good", "#22c55e"),
        <= 100 => ("Moderate", "#eab308"),
        <= 150 => ("Unhealthy · Sensitive", "#f97316"),
        <= 200 => ("Unhealthy", "#ef4444"),
        <= 300 => ("Very Unhealthy", "#a855f7"),
        _ => ("Hazardous", "#dc2626"),
    };
}
