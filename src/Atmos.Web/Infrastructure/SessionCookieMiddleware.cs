using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Atmos.Web.Configuration;
using Microsoft.Extensions.Options;

namespace Atmos.Web.Infrastructure;

/// <summary>
/// Ported from getOrCreateSession()/parseCookies() (weather-server.ts:39-54).
/// Issues (or validates and reuses) a random 32-hex-char "sid" cookie on every
/// request, before any handler runs, and stashes the resolved value in
/// HttpContext.Items for IAppSessionAccessor to read.
/// </summary>
public sealed partial class SessionCookieMiddleware(
    RequestDelegate next, IOptions<SessionCookieOptions> options)
{
    public const string SessionIdItemKey = "Atmos.SessionId";

    private readonly SessionCookieOptions _options = options.Value;

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex SessionIdFormat();

    public async Task InvokeAsync(HttpContext context)
    {
        var existing = context.Request.Cookies[_options.Name];
        string sessionId;

        if (existing is not null && SessionIdFormat().IsMatch(existing))
        {
            sessionId = existing;
        }
        else
        {
            sessionId = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
            context.Response.Cookies.Append(_options.Name, sessionId, new CookieOptions
            {
                Path = "/",
                MaxAge = TimeSpan.FromDays(_options.MaxAgeDays),
                HttpOnly = true,
                Secure = context.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
            });
        }

        context.Items[SessionIdItemKey] = sessionId;
        await next(context);
    }
}
