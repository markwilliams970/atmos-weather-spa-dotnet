namespace Atmos.Web.Data;

public enum UnitsPreference
{
    Imperial,
    Metric
}

public enum LocationType
{
    Zip,
    City,
    Map
}

public sealed class RecentSearch
{
    public int Id { get; set; }
    public required string SessionId { get; set; }
    public required string Label { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double? ElevationMeters { get; set; }
    public UnitsPreference Units { get; set; } = UnitsPreference.Imperial;
    public LocationType LocationType { get; set; } = LocationType.Zip;
    public DateTime CreatedUtc { get; set; }
    public DateTime LastAccessedUtc { get; set; }
}
