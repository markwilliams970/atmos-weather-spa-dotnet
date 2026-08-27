using System.Text.Json.Serialization;

namespace Atmos.Web.Services;

internal sealed class OpenMeteoAirQualityResponse
{
    [JsonPropertyName("current")]
    public OpenMeteoAirQualityCurrent? Current { get; set; }
}

internal sealed class OpenMeteoAirQualityCurrent
{
    [JsonPropertyName("us_aqi")]
    public double UsAqi { get; set; }

    [JsonPropertyName("pm10")]
    public double Pm10 { get; set; }

    [JsonPropertyName("pm2_5")]
    public double Pm25 { get; set; }

    [JsonPropertyName("ozone")]
    public double Ozone { get; set; }

    [JsonPropertyName("nitrogen_dioxide")]
    public double NitrogenDioxide { get; set; }
}
