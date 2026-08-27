using System.Text.Json.Serialization;

namespace Atmos.Web.Services;

internal sealed class OverpassResponse
{
    [JsonPropertyName("elements")]
    public List<OverpassElement>? Elements { get; set; }
}

internal sealed class OverpassElement
{
    [JsonPropertyName("lat")]
    public double? Lat { get; set; }

    [JsonPropertyName("lon")]
    public double? Lon { get; set; }

    [JsonPropertyName("center")]
    public OverpassCenter? Center { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}

internal sealed class OverpassCenter
{
    [JsonPropertyName("lat")]
    public double Lat { get; set; }

    [JsonPropertyName("lon")]
    public double Lon { get; set; }
}

internal sealed class NominatimReverseResponse
{
    [JsonPropertyName("address")]
    public Dictionary<string, string>? Address { get; set; }
}
