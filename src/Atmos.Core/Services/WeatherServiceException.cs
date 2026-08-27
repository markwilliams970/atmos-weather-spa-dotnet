namespace Atmos.Core.Services;

/// <summary>
/// Thrown when the core weather forecast can't be retrieved. Callers should map
/// this to a 502/503 with the message shown as-is — it deliberately never wraps
/// a raw upstream exception message (CLAUDE.md §15).
/// </summary>
public sealed class WeatherServiceException(string message) : Exception(message);
