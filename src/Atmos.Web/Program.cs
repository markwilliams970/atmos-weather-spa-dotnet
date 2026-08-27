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

    var builder = WebApplication.CreateBuilder(args);

    // Reads the entire logging pipeline (sinks, levels, enrichers) from the
    // "Serilog" config section — appsettings.Production.json is the
    // deployment switch that points the file sink at
    // C:\ProgramData\atmos\logs instead of the dev-default relative "logs/".
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

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

// Exposed for WebApplicationFactory<Program> in integration tests.
public partial class Program;
