namespace Atmos.Web.Infrastructure;

/// <summary>
/// Lightweight CSRF mitigation for the one real state-changing endpoint
/// (PUT /api/recent/units), chosen over the full ASP.NET Core antiforgery-token
/// flow per Phase B §15/§20 — proportionate to the low real-world impact (an
/// attacker can only flip one victim's saved unit preference for one label).
/// Real browser fetch() calls always send Origin on state-changing requests
/// (PUT/POST/DELETE) per the Fetch spec's "unsafe request" handling, so a
/// missing or mismatched Origin is treated as untrusted.
/// </summary>
public static class SameOriginCheck
{
    public static bool IsSameOrigin(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
        {
            return false;
        }

        var expected = $"{request.Scheme}://{request.Host}";
        return string.Equals(origin, expected, StringComparison.OrdinalIgnoreCase);
    }
}
