namespace Atmos.Web.Models;

public sealed record AirQuality(
    int UsAqi,
    double Pm25,
    double Pm10,
    int Ozone,
    int No2,
    string Category,
    string Color);
