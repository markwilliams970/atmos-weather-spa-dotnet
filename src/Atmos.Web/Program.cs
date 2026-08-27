using Atmos.Core;
using Atmos.Web.Data;
using Atmos.Web.Endpoints;
using Atmos.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Serilog;

// Bootstrap logger: covers anything that happens before configuration is
// available (including startup failures) — replaced by the fully-configured
// logger below once the host builds. This is Serilog's standard ASP.NET Core
// pattern; see https://github.com/serilog/serilog-aspnetcore.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting Atmos.Web");

    var app = Program.BuildApp(args);
    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    // HostAbortedException is thrown by EF Core's design-time tooling
    // (dotnet ef migrations add/database update), which builds and
    // immediately tears down a host to inspect DI — not a real failure.
    Log.Fatal(ex, "Atmos.Web terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Exposed for WebApplicationFactory<Program> in integration tests (D16), and
// for Atmos.Web.PlaywrightTests (D17) to build a real, Kestrel-listening
// instance directly — WebApplicationFactory's own double-build workaround for
// getting a real socket doesn't work for a minimal-hosting-API Program like
// this one (confirmed by hitting it: the second Build() reuses internal state
// from the first invocation and throws ObjectDisposedException resolving
// DataProtection services), so D17 calls BuildApp itself instead.
public partial class Program
{
    public static WebApplication BuildApp(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        // WebApplication.CreateBuilder(args) alone infers ApplicationName from
        // Assembly.GetEntryAssembly() — correct when Atmos.Web is the process
        // actually running, wrong when this method is called as a library
        // from a different process (Atmos.Web.PlaywrightTests'
        // PlaywrightAppFactory, D17), where it would resolve to "testhost"
        // and the static-web-assets manifest lookup (app.MapStaticAssets()
        // below) would fail outright. Assembly.GetName().Name is always
        // "Atmos.Web" regardless of who loaded the assembly, so this is safe
        // for the real app too — it's the same value CreateBuilder(args)
        // would have inferred on its own there. (ContentRootPath needs no
        // equivalent override: CreateBuilder(args) already resolves it
        // correctly from the executable's own location, not the current
        // directory — the PlaywrightAppFactory caller instead passes an
        // explicit --contentRoot in args, since Assembly.Location isn't
        // reliable for that case: ProjectReference copies Atmos.Web.dll into
        // the *caller's* output directory, which has no wwwroot/ of its own.)
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = typeof(Program).Assembly.GetName().Name,
        });

        // Reads the entire logging pipeline (sinks, levels, enrichers) from the
        // "Serilog" config section — appsettings.Production.json is the
        // deployment switch that points the file sink at
        // C:\ProgramData\atmos\logs instead of the dev-default relative "logs/".
        // preserveStaticLogger: true keeps the static Log.Logger as the plain
        // bootstrap console logger (used only for the startup/shutdown lines
        // in this file) rather than rebinding it to this host's fully-configured
        // logger. Without it, WebApplicationFactory<Program> (D16's integration
        // tests) fails with "the logger is already frozen": the test host's
        // HostFactoryResolver builds and discards a host once to probe the
        // entry point before building the real one, and both builds run in the
        // same process against the same static Log.Logger.
        builder.Host.UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext(),
            preserveStaticLogger: true);

        // Add services to the container.
        builder.Services.AddRazorPages();

        builder.Services.AddDbContext<AtmosDbContext>(options =>
            options.UseSqlServer(builder.Configuration.GetConnectionString("AtmosDb")));

        builder.Services.AddHealthChecks()
            .AddDbContextCheck<AtmosDbContext>();

        builder.Services.AddAtmosCoreServices(builder.Configuration);
        builder.Services.AddAtmosWebServices(builder.Configuration);

        builder.Services.AddExceptionHandler<ApiExceptionHandler>();
        builder.Services.AddProblemDetails();

        // Test-only hook: applied after every real registration above (so it
        // can remove/replace them, e.g. swapping SQL Server for SQLite) and
        // before Build(). No-op in the real app — configure is always null
        // from the top-level Main above.
        configure?.Invoke(builder);

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        app.UseExceptionHandler("/Error");
        if (!app.Environment.IsDevelopment())
        {
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseHttpsRedirection();

        app.UseRouting();

        // One structured log line per request (method, path, status, elapsed) —
        // the closest thing to a "root span" this app has without an actual APM
        // tracer attached. Endpoint handlers enrich it with business context via
        // IDiagnosticContext (e.g. WeatherEndpoints sets LocationType/Zip) rather
        // than logging a second, separate line.
        app.UseSerilogRequestLogging();

        app.UseMiddleware<SessionCookieMiddleware>();

        app.UseAuthorization();

        app.MapStaticAssets();
        app.MapRazorPages()
           .WithStaticAssets();
        app.MapHealthChecks("/healthz");

        app.MapWeatherEndpoints();
        app.MapGeocodeEndpoints();
        app.MapRecentEndpoints();
        app.MapAirQualityEndpoints();
        app.MapElevationEndpoints();
        app.MapNearbyPlaceEndpoints();
        app.MapRadarEndpoints();

        return app;
    }
}
