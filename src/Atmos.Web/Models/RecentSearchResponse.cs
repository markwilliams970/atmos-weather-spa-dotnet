using Atmos.Web.Data;

namespace Atmos.Web.Models;

public sealed record RecentSearchResponse(string Label, double Latitude, double Longitude, double? ElevationMeters, string Units)
{
    public static RecentSearchResponse From(RecentSearch entity) => new(
        entity.Label,
        entity.Latitude,
        entity.Longitude,
        entity.ElevationMeters,
        entity.Units == UnitsPreference.Metric ? "metric" : "imperial");
}

public sealed record RecentSearchUnitsRequest(string Label, string Units);
