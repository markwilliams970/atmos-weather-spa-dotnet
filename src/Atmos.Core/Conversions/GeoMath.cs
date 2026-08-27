namespace Atmos.Core.Conversions;

/// <summary>Great-circle distance, ported from haversineKm (weather-server.ts).</summary>
public static class GeoMath
{
    private const double EarthRadiusKm = 6371;

    public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Pow(Math.Sin(dLat / 2), 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Pow(Math.Sin(dLon / 2), 2);
        return EarthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
