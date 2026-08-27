using System.Runtime.CompilerServices;
using Atmos.Web.Tests.Integration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Atmos.Web.PlaywrightTests;

/// <summary>
/// Builds a real, socket-listening instance of the app for Playwright's
/// browser to navigate to — same SQL-Server-to-SQLite/external-service-to-
/// fake swap as D16's AtmosWebApplicationFactory (via the shared
/// TestHostConfiguration), applied through Program.BuildApp's configure hook
/// directly rather than through WebApplicationFactory&lt;Program&gt;.
///
/// WebApplicationFactory's usual trick for getting a real Kestrel listener —
/// building a second host from the same IHostBuilder with UseKestrel() — does
/// not work here: this app uses the minimal-hosting API (WebApplication.
/// CreateBuilder), so WebApplicationFactory reaches Program through
/// HostFactoryResolver's DeferredHostBuilder, which re-invokes the entry
/// point on every Build() rather than reusing a real, reusable IHostBuilder.
/// Calling Build() on it a second time was confirmed (empirically, D17) to
/// reuse disposed internal state from the first invocation and throw
/// ObjectDisposedException resolving DataProtection services. Calling
/// Program.BuildApp directly sidesteps that entirely — exactly one real
/// WebApplicationBuilder/WebApplication per factory instance.
/// </summary>
public sealed class PlaywrightAppFactory : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private WebApplication? _app;

    // Assembly.Location isn't usable to find Atmos.Web's own wwwroot/ here:
    // the .NET SDK's ProjectReference mechanism copies Atmos.Web.dll into
    // *this* project's own output directory, which has no wwwroot/ of its
    // own. [CallerFilePath] instead bakes in this source file's path at
    // compile time, which survives that copy — safe to rely on here (unlike
    // in Program.cs itself) because test code never gets published/deployed.
    private static readonly string AtmosWebContentRoot = Path.GetFullPath(
        Path.Combine(Path.GetDirectoryName(ThisFilePath())!, "..", "..", "src", "Atmos.Web"));

    private static string ThisFilePath([CallerFilePath] string path = "") => path;

    public string ServerAddress { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();

        // WebApplicationBuilder fixes its environment/content-root at
        // construction time from args/environment variables —
        // WebHost.UseEnvironment(...) on the already-built builder throws
        // "changing the host configuration is not supported," so both have
        // to go in via the command-line switches WebApplication.
        // CreateBuilder(args) itself recognizes. Environment must be
        // "Development" specifically, not e.g. "Testing" — the SDK's static
        // web assets support (MapStaticAssets()'s fingerprinted JS/CSS) only
        // auto-enables when running from build output (not a publish) in the
        // Development environment; anything else 404s every wwwroot file,
        // which silently breaks every one of these tests since app.js never
        // loads. This matches the environment this app has always been
        // manually browser-tested under via `dotnet run`.
        _app = Program.BuildApp(["--environment", "Development", "--contentRoot", AtmosWebContentRoot], builder =>
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            TestHostConfiguration.UseFakesAndSqlite(builder.Services, _connection);
        });

        await _app.StartAsync();

        ServerAddress = _app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    }

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        await _connection.DisposeAsync();
    }
}
