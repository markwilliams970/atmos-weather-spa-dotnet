using Atmos.Web.Configuration;
using Atmos.Web.Services;

namespace Atmos.Web.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAtmosWebServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RecentSearchOptions>()
            .Bind(configuration.GetSection(RecentSearchOptions.SectionName));

        services.AddOptions<SessionCookieOptions>()
            .Bind(configuration.GetSection(SessionCookieOptions.SectionName));

        services.AddHttpContextAccessor();
        services.AddScoped<IAppSessionAccessor, AppSessionAccessor>();
        services.AddScoped<IRecentSearchService, RecentSearchService>();

        // All four of these are non-essential enhancements (CLAUDE.md §12) — none
        // retry on failure, matching the reference app's deliberate "fail fast"
        // policy for exactly this set of calls (Phase B §10).
        services.AddHttpClient<IElevationService, ElevationService>(
            client => client.Timeout = TimeSpan.FromSeconds(6));

        services.AddHttpClient<INearbyPlaceService, NearbyPlaceService>(
            client => client.Timeout = TimeSpan.FromSeconds(10)); // per-call Overpass(6s)/Nominatim(5s) timeouts are enforced inside the service itself

        services.AddHttpClient<IAirQualityService, AirQualityService>(
            client => client.Timeout = TimeSpan.FromSeconds(8));

        services.AddHttpClient<IRadarService, RadarService>(
            client => client.Timeout = TimeSpan.FromSeconds(6));

        return services;
    }
}
