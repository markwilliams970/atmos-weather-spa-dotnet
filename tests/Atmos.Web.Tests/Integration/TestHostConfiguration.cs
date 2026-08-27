using Atmos.Core.Services;
using Atmos.Web.Data;
using Atmos.Web.Services;
using Atmos.Web.Tests.Integration.Fakes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Atmos.Web.Tests.Integration;

/// <summary>
/// The "swap SQL Server for SQLite, swap every external-API-backed service
/// for a deterministic fake" configuration shared by both
/// <see cref="AtmosWebApplicationFactory"/> (D16's in-memory-hosted
/// integration tests) and Atmos.Web.PlaywrightTests' Kestrel-hosted browser
/// tests (D17) — kept in one place so the two suites can't drift apart.
/// </summary>
public static class TestHostConfiguration
{
    public static void UseFakesAndSqlite(IServiceCollection services, SqliteConnection connection)
    {
        // AddDbContext registers its options-configuration action as an
        // IDbContextOptionsConfiguration<T> entry, additive across multiple
        // AddDbContext calls (EF Core applies all of them) — removing only
        // DbContextOptions<T> leaves Program.cs's UseSqlServer(...) delegate
        // in place alongside ours, so EF Core sees two providers configured
        // on the same context and throws.
        services.RemoveAll<DbContextOptions<AtmosDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<AtmosDbContext>>();
        services.RemoveAll<AtmosDbContext>();
        services.AddDbContext<AtmosDbContext>(options => options.UseSqlite(connection));

        services.RemoveAll<IGeocodingService>();
        services.AddSingleton<IGeocodingService, FakeGeocodingService>();

        services.RemoveAll<IWeatherService>();
        services.AddSingleton<IWeatherService, FakeWeatherService>();

        services.RemoveAll<IElevationService>();
        services.AddSingleton<IElevationService, FakeElevationService>();

        services.RemoveAll<INearbyPlaceService>();
        services.AddSingleton<INearbyPlaceService, FakeNearbyPlaceService>();

        services.RemoveAll<IAirQualityService>();
        services.AddSingleton<IAirQualityService, FakeAirQualityService>();

        services.RemoveAll<IRadarService>();
        services.AddSingleton<IRadarService, FakeRadarService>();

        // Builds a throwaway provider purely to create the SQLite schema
        // eagerly, before the real host (and any request) exists — the
        // health check and every test's first request otherwise race
        // against schema creation.
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<AtmosDbContext>().Database.EnsureCreated();
    }
}
