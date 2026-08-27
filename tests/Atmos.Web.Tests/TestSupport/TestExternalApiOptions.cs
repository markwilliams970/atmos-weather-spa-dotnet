using Atmos.Core.Configuration;

namespace Atmos.Web.Tests.TestSupport;

public static class TestExternalApiOptions
{
    public static ExternalApiOptions Create() => new()
    {
        Zippopotam = "https://api.zippopotam.us",
        OpenMeteoForecast = "https://api.open-meteo.com",
        OpenMeteoGeocoding = "https://geocoding-api.open-meteo.com",
        OpenMeteoAirQuality = "https://air-quality-api.open-meteo.com",
        OpenMeteoElevation = "https://api.open-meteo.com",
        Overpass = "https://overpass-api.de",
        Nominatim = "https://nominatim.openstreetmap.org",
        NominatimUserAgent = "AtmosWeather-Tests/1.0",
        RainViewer = "https://api.rainviewer.com",
    };
}
