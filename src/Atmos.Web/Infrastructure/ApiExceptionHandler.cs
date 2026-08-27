using Atmos.Core.Services;
using Atmos.Web.Models;
using Microsoft.AspNetCore.Diagnostics;

namespace Atmos.Web.Infrastructure;

/// <summary>
/// Global unhandled-exception -> response mapping for the /api/* JSON surface,
/// per CLAUDE.md §15's four-way distinction. Page requests fall through to the
/// default "/Error" redirect configured in Program.cs — this handler only
/// claims requests under /api.
/// </summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // UseExceptionHandler("/Error") rewrites HttpContext.Request.Path to
        // the fallback path *before* invoking any registered IExceptionHandler
        // (confirmed by D16's integration tests, which caught this: every
        // /api/* failure was silently falling through to the generic 500
        // rather than this handler's status-code mapping, because the /api
        // check below was matching against "/Error", not the real request
        // path). IExceptionHandlerPathFeature.Path is the one populated
        // before that rewrite happens.
        var originalPath = httpContext.Features.Get<IExceptionHandlerPathFeature>()?.Path ?? httpContext.Request.Path;
        if (!originalPath.StartsWith("/api", StringComparison.Ordinal))
        {
            return false;
        }

        var (statusCode, message) = exception switch
        {
            // Unavailable core weather data — never forward the raw upstream
            // exception message to the client (CLAUDE.md §15).
            WeatherServiceException => (StatusCodes.Status502BadGateway, "Weather data is temporarily unavailable."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred."),
        };

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception on {Path}", originalPath);
        }
        else
        {
            logger.LogWarning(exception, "{ExceptionType} on {Path}", exception.GetType().Name, originalPath);
        }

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(new ApiErrorResponse(message), cancellationToken);
        return true;
    }
}
