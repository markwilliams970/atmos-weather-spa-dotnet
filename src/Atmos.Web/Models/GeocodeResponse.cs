using Atmos.Core.Models;

namespace Atmos.Web.Models;

public sealed record GeocodeResponse(IReadOnlyList<GeocodeResult> Results);
