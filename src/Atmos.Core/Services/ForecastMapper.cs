using System.Globalization;
using Atmos.Core.Conversions;
using Atmos.Core.Models;
using Atmos.Core.Wmo;

namespace Atmos.Core.Services;

/// <summary>
/// Pure shaping logic extracted from fetchWeather (weather-server.ts:166-271) —
/// the single most complex piece of logic in the reference app, per its own
/// engineering notes. Kept separate from the HTTP-calling WeatherService so it
/// can be unit-tested against fixture JSON with no network involved.
/// </summary>
internal static class ForecastMapper
{
    private static readonly string[] DayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    public static WeatherForecast Map(
        OpenMeteoForecastResponse response, Location location, double? elevationMeters)
    {
        var current = response.Current ?? throw new InvalidOperationException("Forecast response has no current conditions.");
        var hourly = response.Hourly ?? new OpenMeteoHourly();
        var daily = response.Daily ?? new OpenMeteoDaily();

        var currentCondition = WmoCodes.Lookup(current.WeatherCode);

        // Locate "now" in the hourly array by matching the YYYY-MM-DDTHH prefix,
        // then slice 24 hours forward — exactly as the reference app does, rather
        // than trusting hourly[0] to already be "now".
        var currentHourPrefix = SafeSubstring(current.Time, 0, 13);
        var currentIndex = hourly.Time.FindIndex(t => SafeSubstring(t, 0, 13) == currentHourPrefix);
        var start = currentIndex >= 0 ? currentIndex : 0;

        var hourlySlots = new List<HourlySlot>();
        for (var i = 0; i < 24 && start + i < hourly.Time.Count; i++)
        {
            var ai = start + i;
            var tempC = GetOrDefault(hourly.Temperature2m, ai, 0);
            hourlySlots.Add(new HourlySlot(
                TimeLabel: i == 0 ? "Now" : FormatHour(hourly.Time[ai]),
                IsCurrent: i == 0,
                TempF: UnitConversions.CelsiusToFahrenheit(tempC),
                TempC: (int)UnitConversions.JsRound(tempC),
                PrecipProb: GetOrDefault(hourly.PrecipitationProbability, ai, 0),
                Emoji: WmoCodes.Lookup(GetOrDefault(hourly.WeatherCode, ai, 0)).Emoji,
                IsDay: GetOrDefault(hourly.IsDay, ai, 1) == 1));
        }

        var dailyRows = new List<DailyRow>();
        for (var i = 0; i < daily.Time.Count; i++)
        {
            var dateStr = daily.Time[i];
            var dayOfWeek = ParseDate(dateStr).DayOfWeek;
            var condition = WmoCodes.Lookup(GetOrDefault(daily.WeatherCode, i, 0));
            var highC = GetOrDefault(daily.Temperature2mMax, i, 0);
            var lowC = GetOrDefault(daily.Temperature2mMin, i, 0);
            var precipSum = GetOrDefault(daily.PrecipitationSum, i, 0);

            dailyRows.Add(new DailyRow(
                DayName: i == 0 ? "Today" : i == 1 ? "Tmrw" : DayNames[(int)dayOfWeek],
                DateLabel: ParseDate(dateStr).ToString("MMM d", CultureInfo.InvariantCulture),
                HighF: UnitConversions.CelsiusToFahrenheit(highC),
                HighC: (int)UnitConversions.JsRound(highC),
                LowF: UnitConversions.CelsiusToFahrenheit(lowC),
                LowC: (int)UnitConversions.JsRound(lowC),
                Emoji: condition.Emoji,
                Condition: condition.Label,
                PrecipMm: UnitConversions.JsRound(precipSum * 10) / 10,
                PrecipIn: UnitConversions.MillimetersToInches(precipSum),
                PrecipProbMax: GetOrDefault(daily.PrecipitationProbabilityMax, i, 0),
                UvMax: GetOrDefault(daily.UvIndexMax, i, 0),
                WindMaxMph: UnitConversions.KmhToMph(GetOrDefault(daily.WindSpeed10mMax, i, 0)),
                WindMaxKmh: (int)UnitConversions.JsRound(GetOrDefault(daily.WindSpeed10mMax, i, 0))));
        }

        var sunrise = ParseHourMinute(daily.Sunrise.Count > 0 ? daily.Sunrise[0] : "");
        var sunset = ParseHourMinute(daily.Sunset.Count > 0 ? daily.Sunset[0] : "");

        var todayHighC = daily.Temperature2mMax.Count > 0 ? daily.Temperature2mMax[0] : current.Temperature2m;
        var todayLowC = daily.Temperature2mMin.Count > 0 ? daily.Temperature2mMin[0] : current.Temperature2m;

        return new WeatherForecast(
            Location: $"{location.City}, {location.State}",
            Zip: "",
            Latitude: location.Latitude,
            Longitude: location.Longitude,
            TempF: UnitConversions.CelsiusToFahrenheit(current.Temperature2m),
            TempC: (int)UnitConversions.JsRound(current.Temperature2m),
            FeelsLikeF: UnitConversions.CelsiusToFahrenheit(current.ApparentTemperature),
            FeelsLikeC: (int)UnitConversions.JsRound(current.ApparentTemperature),
            Humidity: (int)UnitConversions.JsRound(current.RelativeHumidity2m),
            WindMph: UnitConversions.KmhToMph(current.WindSpeed10m),
            WindKmh: (int)UnitConversions.JsRound(current.WindSpeed10m),
            WindDir: UnitConversions.DegreesToCompass(current.WindDirection10m),
            WindDeg: current.WindDirection10m,
            PrecipIn: UnitConversions.MillimetersToInches(current.Precipitation),
            PrecipMm: UnitConversions.JsRound(current.Precipitation * 10) / 10,
            UvIndex: current.UvIndex,
            Condition: currentCondition.Label,
            ConditionEmoji: currentCondition.Emoji,
            Sunrise: FormatTime(sunrise),
            Sunset: FormatTime(sunset),
            SunriseMin: sunrise.Hour * 60 + sunrise.Minute,
            SunsetMin: sunset.Hour * 60 + sunset.Minute,
            IsDay: current.IsDay == 1,
            TodayHighF: UnitConversions.CelsiusToFahrenheit(todayHighC),
            TodayHighC: (int)UnitConversions.JsRound(todayHighC),
            TodayLowF: UnitConversions.CelsiusToFahrenheit(todayLowC),
            TodayLowC: (int)UnitConversions.JsRound(todayLowC),
            Hourly: hourlySlots,
            Daily: dailyRows,
            ElevationMeters: elevationMeters);
    }

    /// <summary>
    /// Formats an hour as "12am"/"1pm"/etc — ported from fmtHour, which reads the
    /// hour digits directly out of the ISO string rather than parsing a Date, to
    /// sidestep any timezone reinterpretation.
    /// </summary>
    private static string FormatHour(string iso)
    {
        var hour = int.Parse(SafeSubstring(iso, 11, 2), CultureInfo.InvariantCulture);
        return hour switch
        {
            0 => "12am",
            12 => "12pm",
            < 12 => $"{hour}am",
            _ => $"{hour - 12}pm",
        };
    }

    private static string FormatTime((int Hour, int Minute) t)
    {
        var period = t.Hour < 12 ? "AM" : "PM";
        var hour12 = t.Hour % 12 == 0 ? 12 : t.Hour % 12;
        return $"{hour12}:{t.Minute:D2} {period}";
    }

    /// <summary>
    /// Open-Meteo's "timezone=auto" daily sunrise/sunset strings carry no offset
    /// (e.g. "2026-08-27T06:45") — they're already local wall-clock time for the
    /// searched location. Reading the digits directly avoids any server-timezone
    /// reinterpretation that parsing into a DateTime/DateTimeOffset could invite.
    /// </summary>
    private static (int Hour, int Minute) ParseHourMinute(string iso)
    {
        if (iso.Length < 16)
        {
            return (0, 0);
        }

        var hour = int.Parse(SafeSubstring(iso, 11, 2), CultureInfo.InvariantCulture);
        var minute = int.Parse(SafeSubstring(iso, 14, 2), CultureInfo.InvariantCulture);
        return (hour, minute);
    }

    private static DateTime ParseDate(string dateOnly) =>
        DateTime.ParseExact(dateOnly, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string SafeSubstring(string s, int start, int length) =>
        start >= s.Length ? "" : s.Substring(start, Math.Min(length, s.Length - start));

    private static double GetOrDefault(List<double> list, int index, double fallback) =>
        index >= 0 && index < list.Count ? list[index] : fallback;

    private static int GetOrDefault(List<int> list, int index, int fallback) =>
        index >= 0 && index < list.Count ? list[index] : fallback;
}
