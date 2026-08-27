namespace Atmos.Web.Models;

/// <summary>
/// Shared error shape for every /api/* endpoint — deliberately keeps the
/// reference app's exact {"error": "..."} field name so no client JS parsing
/// logic needs to change (Phase B §11).
/// </summary>
public sealed record ApiErrorResponse(string Error);
