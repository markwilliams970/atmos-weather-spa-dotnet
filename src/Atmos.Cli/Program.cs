using System.Text.RegularExpressions;
using Atmos.Cli;
using Atmos.Core;
using Atmos.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Ported from weather.ts's main() (weather.ts:185-204). No server, no
// database, no session — a fully independent tool sharing IGeocodingService/
// IWeatherService (and, transitively, the WMO table and unit-conversion
// helpers) with Atmos.Web via Atmos.Core, rather than duplicating them
// locally the way the reference app's weather.ts deliberately did
// (Claude.md's "isolation, not an accidental DRY violation" — CLAUDE.md §35
// asks the .NET port to close exactly this kind of duplication when the CLI
// is retained).
var zip = args.Length > 0 ? args[0].Trim() : null;
if (string.IsNullOrEmpty(zip) || !ZipFormat().IsMatch(zip))
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("  Usage: atmos <5-digit-zip>");
    Console.Error.WriteLine("  Example: atmos 80002");
    Console.Error.WriteLine();
    return 1;
}

// HostApplicationBuilder resolves appsettings.json relative to the current
// working directory by default — fine for a web app always launched from
// its own folder, wrong for a CLI meant to be run from wherever the user
// happens to be. Pin the content root to the executable's own directory so
// `atmos 80002` finds its config regardless of the caller's cwd.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// A one-shot terminal tool has no use for the request-scoped, file-backed
// logging Atmos.Web sets up (docs/logging.md) — just enough console output
// to see a failure, nothing that would clutter the boxed weather output.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Warning);

// ExternalApiOptions is shared as one class across every external service
// (CLAUDE.md's Service Boundaries), including the four (AirQuality,
// Elevation, NearbyPlace, Radar) this CLI never calls — appsettings.json
// still has to supply all of them, or options validation fails at startup.
builder.Services.AddAtmosCoreServices(builder.Configuration);

using var host = builder.Build();

var geocoding = host.Services.GetRequiredService<IGeocodingService>();
var weather = host.Services.GetRequiredService<IWeatherService>();

Console.WriteLine($"\n  Fetching weather for ZIP {zip}…");

try
{
    var location = await geocoding.LookupZipAsync(zip, CancellationToken.None);
    if (location is null)
    {
        Console.Error.WriteLine($"\n  ❌  ZIP code \"{zip}\" not found.\n");
        return 1;
    }

    var forecast = await weather.GetForecastAsync(location, elevationMeters: null, CancellationToken.None);
    ConsoleDisplay.Show(location, forecast);
    return 0;
}
catch (WeatherServiceException ex)
{
    Console.Error.WriteLine($"\n  ❌  {ex.Message}\n");
    return 1;
}

internal static partial class Program
{
    [GeneratedRegex(@"^\d{5}$")]
    private static partial Regex ZipFormat();
}
