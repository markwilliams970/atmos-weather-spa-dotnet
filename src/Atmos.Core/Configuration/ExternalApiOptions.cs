using System.ComponentModel.DataAnnotations;

namespace Atmos.Core.Configuration;

/// <summary>
/// Base URLs (and the one required header) for every external service Atmos calls.
/// Bound from the "ExternalApis" configuration section — see CLAUDE.md §13: never
/// hardcode these, even though today's APIs are all keyless. [Required] here means
/// a missing value fails fast at startup rather than surfacing as a confusing
/// null-reference deep inside a service later.
/// </summary>
public sealed class ExternalApiOptions
{
    public const string SectionName = "ExternalApis";

    [Required] public required string Zippopotam { get; set; }
    [Required] public required string OpenMeteoForecast { get; set; }
    [Required] public required string OpenMeteoGeocoding { get; set; }
    [Required] public required string OpenMeteoAirQuality { get; set; }
    [Required] public required string OpenMeteoElevation { get; set; }
    [Required] public required string Overpass { get; set; }
    [Required] public required string Nominatim { get; set; }
    [Required] public required string NominatimUserAgent { get; set; }
    [Required] public required string RainViewer { get; set; }
}
