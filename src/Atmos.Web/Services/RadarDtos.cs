using System.Text.Json.Serialization;

namespace Atmos.Web.Services;

internal sealed class RainViewerResponse
{
    [JsonPropertyName("host")]
    public string? Host { get; set; }

    [JsonPropertyName("radar")]
    public RainViewerRadar? Radar { get; set; }
}

internal sealed class RainViewerRadar
{
    [JsonPropertyName("past")]
    public List<RainViewerFrame>? Past { get; set; }
}

internal sealed class RainViewerFrame
{
    [JsonPropertyName("time")]
    public long Time { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";
}
