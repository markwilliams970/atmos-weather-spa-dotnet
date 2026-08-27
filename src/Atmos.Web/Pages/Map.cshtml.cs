using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Atmos.Web.Pages;

/// <summary>
/// Secondary deep-link entry point (Phase B decision #2/#4). The primary,
/// day-to-day interaction is still the same-page JS overlay launched from the
/// search dropdown wherever the user is — this page exists so "/map" itself
/// is a real, bookmarkable URL that opens the same overlay on load.
/// </summary>
public class MapModel : PageModel
{
    public void OnGet()
    {
    }
}
