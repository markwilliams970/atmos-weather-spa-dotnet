using Atmos.Web.Data;
using Atmos.Web.Infrastructure;
using Atmos.Web.Models;
using Atmos.Web.Services;
using Serilog;

namespace Atmos.Web.Endpoints;

public static class RecentSearchEndpoints
{
    public static void MapRecentEndpoints(this WebApplication app)
    {
        app.MapGet("/api/recent", GetRecentAsync);
        app.MapPut("/api/recent/units", UpdateUnitsAsync);
    }

    private static async Task<IReadOnlyList<RecentSearchResponse>> GetRecentAsync(
        IRecentSearchService recentSearch, IAppSessionAccessor session, CancellationToken cancellationToken)
    {
        var recent = await recentSearch.GetRecentAsync(session.SessionId, cancellationToken);
        return recent.Select(RecentSearchResponse.From).ToList();
    }

    private static async Task<IResult> UpdateUnitsAsync(
        HttpRequest httpRequest,
        RecentSearchUnitsRequest request,
        IRecentSearchService recentSearch,
        IAppSessionAccessor session,
        IDiagnosticContext diagnosticContext,
        ILogger<RecentSearchEndpointsMarker> logger,
        CancellationToken cancellationToken)
    {
        if (!SameOriginCheck.IsSameOrigin(httpRequest))
        {
            // A cross-origin attempt to flip a unit preference — low-impact,
            // but exactly the kind of security-relevant rejection worth
            // keeping visible once real request tracing exists.
            logger.LogWarning(
                "Rejected cross-origin PUT /api/recent/units from Origin {Origin}",
                httpRequest.Headers.Origin.ToString());
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrEmpty(request.Label))
        {
            return Results.BadRequest(new ApiErrorResponse("Label is required."));
        }

        diagnosticContext.Set("Label", request.Label);
        diagnosticContext.Set("Units", request.Units);

        var units = request.Units == "metric" ? UnitsPreference.Metric : UnitsPreference.Imperial;
        await recentSearch.UpdateUnitsAsync(session.SessionId, request.Label, units, cancellationToken);

        return Results.NoContent();
    }
}

/// <summary>Logger category marker — these are static handlers, not a class instance, so there's no natural type to name the category after.</summary>
public sealed class RecentSearchEndpointsMarker;
