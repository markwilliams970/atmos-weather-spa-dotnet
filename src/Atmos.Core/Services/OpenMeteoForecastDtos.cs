using System.Text.Json.Serialization;

namespace Atmos.Core.Services;

internal sealed class OpenMeteoForecastResponse
{
    [JsonPropertyName("current")]
    public OpenMeteoCurrent? Current { get; set; }

    [JsonPropertyName("hourly")]
    public OpenMeteoHourly? Hourly { get; set; }

    [JsonPropertyName("daily")]
    public OpenMeteoDaily? Daily { get; set; }
}

internal sealed class OpenMeteoCurrent
{
    [JsonPropertyName("time")]
    public string Time { get; set; } = "";

    [JsonPropertyName("temperature_2m")]
    public double Temperature2m { get; set; }

    [JsonPropertyName("apparent_temperature")]
    public double ApparentTemperature { get; set; }

    [JsonPropertyName("relative_humidity_2m")]
    public double RelativeHumidity2m { get; set; }

    [JsonPropertyName("wind_speed_10m")]
    public double WindSpeed10m { get; set; }

    [JsonPropertyName("wind_direction_10m")]
    public double WindDirection10m { get; set; }

    [JsonPropertyName("precipitation")]
    public double Precipitation { get; set; }

    [JsonPropertyName("uv_index")]
    public double UvIndex { get; set; }

    [JsonPropertyName("weather_code")]
    public int WeatherCode { get; set; }

    [JsonPropertyName("is_day")]
    public int IsDay { get; set; }
}

internal sealed class OpenMeteoHourly
{
    [JsonPropertyName("time")]
    public List<string> Time { get; set; } = [];

    [JsonPropertyName("temperature_2m")]
    public List<double> Temperature2m { get; set; } = [];

    [JsonPropertyName("precipitation_probability")]
    public List<int> PrecipitationProbability { get; set; } = [];

    [JsonPropertyName("weather_code")]
    public List<int> WeatherCode { get; set; } = [];

    [JsonPropertyName("is_day")]
    public List<int> IsDay { get; set; } = [];
}

internal sealed class OpenMeteoDaily
{
    [JsonPropertyName("time")]
    public List<string> Time { get; set; } = [];

    [JsonPropertyName("temperature_2m_max")]
    public List<double> Temperature2mMax { get; set; } = [];

    [JsonPropertyName("temperature_2m_min")]
    public List<double> Temperature2mMin { get; set; } = [];

    [JsonPropertyName("weather_code")]
    public List<int> WeatherCode { get; set; } = [];

    [JsonPropertyName("precipitation_sum")]
    public List<double> PrecipitationSum { get; set; } = [];

    [JsonPropertyName("precipitation_probability_max")]
    public List<int> PrecipitationProbabilityMax { get; set; } = [];

    [JsonPropertyName("sunrise")]
    public List<string> Sunrise { get; set; } = [];

    [JsonPropertyName("sunset")]
    public List<string> Sunset { get; set; } = [];

    [JsonPropertyName("uv_index_max")]
    public List<double> UvIndexMax { get; set; } = [];

    [JsonPropertyName("wind_speed_10m_max")]
    public List<double> WindSpeed10mMax { get; set; } = [];
}
