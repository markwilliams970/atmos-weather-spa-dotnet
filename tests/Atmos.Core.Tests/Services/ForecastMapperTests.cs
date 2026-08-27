using System.Text.Json;
using Atmos.Core.Models;
using Atmos.Core.Services;

namespace Atmos.Core.Tests.Services;

public class ForecastMapperTests
{
    // A trimmed-but-realistic Open-Meteo /v1/forecast response, modeled on a real
    // captured response shape. "current.time" (2026-08-27T14:00) deliberately
    // lands on the 15th hourly slot (index 14) to exercise the "find now inside
    // the hourly array, then slice 24 forward" logic (weather-server.ts:207-209)
    // rather than trivially matching index 0.
    private const string FixtureJson = """
        {
          "current": {
            "time": "2026-08-27T14:00",
            "temperature_2m": 22.4,
            "apparent_temperature": 21.8,
            "relative_humidity_2m": 48,
            "wind_speed_10m": 14.2,
            "wind_direction_10m": 270,
            "precipitation": 0.0,
            "uv_index": 4.6,
            "weather_code": 2,
            "is_day": 1
          },
          "hourly": {
            "time": ["2026-08-27T00:00","2026-08-27T01:00","2026-08-27T02:00","2026-08-27T03:00","2026-08-27T04:00","2026-08-27T05:00","2026-08-27T06:00","2026-08-27T07:00","2026-08-27T08:00","2026-08-27T09:00","2026-08-27T10:00","2026-08-27T11:00","2026-08-27T12:00","2026-08-27T13:00","2026-08-27T14:00","2026-08-27T15:00","2026-08-27T16:00","2026-08-27T17:00","2026-08-27T18:00","2026-08-27T19:00","2026-08-27T20:00","2026-08-27T21:00","2026-08-27T22:00","2026-08-27T23:00","2026-08-28T00:00","2026-08-28T01:00"],
            "temperature_2m": [15,14,13,13,12,12,13,15,17,19,20,21,22,22,22.4,23,23,22,20,18,17,16,15,15,14,14],
            "precipitation_probability": [0,0,0,0,0,0,0,0,0,0,5,5,10,10,10,5,5,0,0,0,0,0,0,0,0,0],
            "weather_code": [1,1,1,1,1,1,1,1,2,2,2,2,2,2,2,2,2,1,1,0,0,0,0,0,0,0],
            "is_day": [0,0,0,0,0,0,0,1,1,1,1,1,1,1,1,1,1,1,1,1,0,0,0,0,0,0]
          },
          "daily": {
            "time": ["2026-08-27","2026-08-28","2026-08-29","2026-08-30","2026-08-31","2026-09-01","2026-09-02"],
            "temperature_2m_max": [23,25,24,22,21,20,19],
            "temperature_2m_min": [12,13,14,12,11,10,9],
            "weather_code": [2,1,61,95,3,0,0],
            "precipitation_sum": [0,0,3.2,12.7,0,0,0],
            "precipitation_probability_max": [10,5,60,90,20,0,0],
            "sunrise": ["2026-08-27T06:22","2026-08-28T06:23","2026-08-29T06:24","2026-08-30T06:25","2026-08-31T06:26","2026-09-01T06:27","2026-09-02T06:28"],
            "sunset": ["2026-08-27T19:47","2026-08-28T19:45","2026-08-29T19:44","2026-08-30T19:42","2026-08-31T19:40","2026-09-01T19:38","2026-09-02T19:37"],
            "uv_index_max": [6.1,6.0,5.8,4.2,3.9,5.5,6.2],
            "wind_speed_10m_max": [18.3,20.1,25.6,30.2,15.4,12.1,14.8]
          }
        }
        """;

    private static WeatherForecast MapFixture(double? elevationMeters = null)
    {
        var response = JsonSerializer.Deserialize<OpenMeteoForecastResponse>(FixtureJson)!;
        var location = new Location("Boulder", "CO", 40.0150, -105.2705);
        return ForecastMapper.Map(response, location, elevationMeters);
    }

    [Fact]
    public void Maps_current_conditions_with_reference_rounding_and_conversions()
    {
        var forecast = MapFixture();

        Assert.Equal("Boulder, CO", forecast.Location);
        Assert.Equal(72, forecast.TempF);   // JsRound(22.4*9/5+32) = 72.32 -> 72
        Assert.Equal(22, forecast.TempC);   // JsRound(22.4) = 22
        Assert.Equal(71, forecast.FeelsLikeF);
        Assert.Equal(48, forecast.Humidity);
        Assert.Equal("W", forecast.WindDir); // 270 degrees
        Assert.Equal("Partly Cloudy", forecast.Condition); // WMO code 2
        Assert.True(forecast.IsDay);
    }

    [Fact]
    public void Finds_current_hour_by_prefix_match_not_by_assuming_index_zero()
    {
        var forecast = MapFixture();

        // current.time is 2026-08-27T14:00, which is hourly.time[14] — the
        // mapper must locate that slot as "Now", not just use hourly[0].
        var nowSlot = forecast.Hourly[0];
        Assert.Equal("Now", nowSlot.TimeLabel);
        Assert.True(nowSlot.IsCurrent);
        Assert.Equal(72, nowSlot.TempF); // hourly.temperature_2m[14] == 22.4, same as current
    }

    [Fact]
    public void Slices_24_hours_forward_from_the_current_hour()
    {
        var forecast = MapFixture();

        Assert.Equal(12, forecast.Hourly.Count); // fixture only has 12 slots from index 14 onward
        Assert.Equal("1am", forecast.Hourly[^1].TimeLabel); // last slot is 2026-08-28T01:00
    }

    [Fact]
    public void Labels_first_two_daily_rows_today_and_tomorrow_then_weekday_names()
    {
        var forecast = MapFixture();

        Assert.Equal("Today", forecast.Daily[0].DayName);
        Assert.Equal("Tmrw", forecast.Daily[1].DayName);

        var expectedWeekday = new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" }[(int)new DateTime(2026, 8, 29).DayOfWeek];
        Assert.Equal(expectedWeekday, forecast.Daily[2].DayName);
    }

    [Fact]
    public void Formats_daily_date_label_as_month_day()
    {
        var forecast = MapFixture();

        Assert.Equal("Aug 27", forecast.Daily[0].DateLabel);
        Assert.Equal("Sep 2", forecast.Daily[6].DateLabel);
    }

    [Fact]
    public void Computes_sunrise_sunset_minute_of_day_from_literal_iso_digits()
    {
        var forecast = MapFixture();

        // "2026-08-27T06:22" -> 6*60+22 = 382, with no timezone reinterpretation.
        Assert.Equal(6 * 60 + 22, forecast.SunriseMin);
        Assert.Equal(19 * 60 + 47, forecast.SunsetMin);
        Assert.Equal("6:22 AM", forecast.Sunrise);
        Assert.Equal("7:47 PM", forecast.Sunset);
    }

    [Fact]
    public void Passes_through_elevation_when_provided_and_omits_it_when_not()
    {
        Assert.Equal(1655.0, MapFixture(elevationMeters: 1655.0).ElevationMeters);
        Assert.Null(MapFixture().ElevationMeters);
    }

    [Fact]
    public void Maps_daily_precipitation_and_wind_conversions()
    {
        var forecast = MapFixture();
        var stormyDay = forecast.Daily[3]; // 2026-08-30, precipitation_sum 12.7mm, weather_code 95

        Assert.Equal("Thunderstorm", stormyDay.Condition);
        Assert.Equal(12.7, stormyDay.PrecipMm, 1);
        Assert.Equal(90, stormyDay.PrecipProbMax);
        Assert.Equal(19, stormyDay.WindMaxMph); // JsRound(30.2 * 0.621371) = 18.77 -> 19
    }
}
