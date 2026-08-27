namespace Atmos.Web.Infrastructure;

/// <summary>
/// Reads the per-request session id resolved by <see cref="SessionCookieMiddleware"/>.
/// There's no ASP.NET Core ISession (server-side session-state bag) involved —
/// the app has nothing to store there; the cookie value is just a correlation
/// key scoping RecentSearch rows, exactly as in the reference app.
/// </summary>
public interface IAppSessionAccessor
{
    string SessionId { get; }
}
