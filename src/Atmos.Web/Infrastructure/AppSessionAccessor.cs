using Microsoft.AspNetCore.Http;

namespace Atmos.Web.Infrastructure;

public sealed class AppSessionAccessor(IHttpContextAccessor httpContextAccessor) : IAppSessionAccessor
{
    public string SessionId =>
        httpContextAccessor.HttpContext?.Items[SessionCookieMiddleware.SessionIdItemKey] as string
        ?? throw new InvalidOperationException(
            $"No session id on the current request — is {nameof(SessionCookieMiddleware)} registered?");
}
