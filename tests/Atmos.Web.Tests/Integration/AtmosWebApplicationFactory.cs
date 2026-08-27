using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

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

        builder.ConfigureServices(services => TestHostConfiguration.UseFakesAndSqlite(services, _connection));
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
