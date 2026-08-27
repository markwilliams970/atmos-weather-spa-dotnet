using Atmos.Core.Services;
using Atmos.Web.Data;
using Atmos.Web.Services;
using Atmos.Web.Tests.Integration.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Atmos.Web.Tests.Integration;

/// <summary>
/// Boots the real ASP.NET Core pipeline (routing, model binding, session
/// middleware, exception handling) end to end, swapping out only the two
/// things a test suite should never depend on: SQL Server (replaced with an
/// EF Core SQLite connection, same fidelity trade-off as
/// RecentSearchServiceTests) and the external-API-backed services (replaced
/// with the deterministic fakes in Fakes/ — their real HTTP-parsing logic is
/// already covered by the Services/ unit tests).
///
/// One instance is shared per test class via IClassFixture&lt;T&gt;; the SQLite
/// connection (and its schema) lives for the whole class. Tests stay isolated
/// from each other despite sharing the database because every test creates
/// its own HttpClient (via CreateClient()), which means its own session
/// cookie, and RecentSearch rows are always scoped by session id.
/// </summary>
public sealed class AtmosWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public AtmosWebApplicationFactory() => _connection.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // AddDbContext registers its options-configuration action as an
            // IDbContextOptionsConfiguration<T> entry, additive across
            // multiple AddDbContext calls (EF Core applies all of them) —
            // removing only DbContextOptions<T> leaves Program.cs's
            // UseSqlServer(...) delegate in place alongside ours, so EF Core
            // sees two providers configured on the same context and throws.
            services.RemoveAll<DbContextOptions<AtmosDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AtmosDbContext>>();
            services.RemoveAll<AtmosDbContext>();
            services.AddDbContext<AtmosDbContext>(options => options.UseSqlite(_connection));

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
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
