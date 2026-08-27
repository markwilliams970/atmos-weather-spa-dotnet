using Atmos.Web.Models;

namespace Atmos.Web.Services;

public interface IAirQualityService
{
    Task<AirQuality> GetAirQualityAsync(double latitude, double longitude, CancellationToken cancellationToken);
}
