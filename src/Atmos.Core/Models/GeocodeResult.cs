namespace Atmos.Core.Models;

public sealed record GeocodeResult(
    string Name,
    string? Admin1,
    string? CountryCode,
    double Latitude,
    double Longitude);
