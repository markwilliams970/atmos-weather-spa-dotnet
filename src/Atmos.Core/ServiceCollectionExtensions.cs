using Atmos.Core.Configuration;
using Atmos.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Atmos.Core;

/// <summary>
/// Registers the services shared by Atmos.Web and Atmos.Cli. Both hosts call
/// this from their own composition root rather than duplicating the same
/// AddHttpClient wiring (CLAUDE.md §35).
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAtmosCoreServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ExternalApiOptions>()
            .Bind(configuration.GetSection(ExternalApiOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Zippopotam (ZIP lookup) and Open-Meteo geocoding (city autocomplete) are
        // both core-path search entry points, so a single retry on transient
        // failures is worthwhile for either; per Phase B §10 the Open-Meteo
        // forecast call gets the same treatment.
        services.AddHttpClient<IGeocodingService, GeocodingService>()
            .AddResilienceHandler("geocoding", ConfigureCorePathResilience);

        services.AddHttpClient<IWeatherService, WeatherService>()
            .AddResilienceHandler("weather", ConfigureCorePathResilience);

        return services;
    }

    private static void ConfigureCorePathResilience(ResiliencePipelineBuilder<HttpResponseMessage> builder)
    {
        builder.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 1,
            Delay = TimeSpan.FromMilliseconds(300),
            BackoffType = DelayBackoffType.Constant,
        });
        builder.AddTimeout(TimeSpan.FromSeconds(8));
    }
}
