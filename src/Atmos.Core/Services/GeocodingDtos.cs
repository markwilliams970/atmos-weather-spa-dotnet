using System.Text.Json.Serialization;

namespace Atmos.Core.Services;

// External API DTOs, kept private to this assembly's Services layer per CLAUDE.md §5/§10 —
// never exposed directly to Razor Pages, the JSON API surface, or Atmos.Cli.

internal sealed class ZippopotamResponse
{
    [JsonPropertyName("places")]
    public List<ZippopotamPlace>? Places { get; set; }
}

internal sealed class ZippopotamPlace
{
    [JsonPropertyName("place name")]
    public string? PlaceName { get; set; }

    [JsonPropertyName("state abbreviation")]
    public string? StateAbbreviation { get; set; }

    [JsonPropertyName("latitude")]
    public string? Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public string? Longitude { get; set; }
}

internal sealed class OpenMeteoGeocodingResponse
{
    [JsonPropertyName("results")]
    public List<OpenMeteoGeocodingResult>? Results { get; set; }
}

internal sealed class OpenMeteoGeocodingResult
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("admin1")]
    public string? Admin1 { get; set; }

    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }

    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }
}
