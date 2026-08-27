using Atmos.Web.Data;
using Atmos.Web.Infrastructure;
using Atmos.Web.Models;
using Atmos.Web.Services;

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
        CancellationToken cancellationToken)
    {
        if (!SameOriginCheck.IsSameOrigin(httpRequest))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrEmpty(request.Label))
        {
            return Results.BadRequest(new ApiErrorResponse("Label is required."));
        }

        var units = request.Units == "metric" ? UnitsPreference.Metric : UnitsPreference.Imperial;
        await recentSearch.UpdateUnitsAsync(session.SessionId, request.Label, units, cancellationToken);

        return Results.NoContent();
    }
}
