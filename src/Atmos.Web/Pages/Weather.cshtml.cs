using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Atmos.Web.Pages;

public class WeatherModel : PageModel
{
    public IActionResult OnGet(string? zip, string? lat, string? lon, string? label)
    {
        var hasZip = !string.IsNullOrEmpty(zip);
        var hasLatLonLabel = !string.IsNullOrEmpty(lat) && !string.IsNullOrEmpty(lon) && !string.IsNullOrEmpty(label);

        if (!hasZip && !hasLatLonLabel)
        {
            return RedirectToPage("/Index");
        }

        // Rendering itself is identical to Index — the shared layout's
        // wx-panels markup is already present; app.js's router reads the
        // query string on load and fetches+populates it (Phase B §4).
        return Page();
    }
}
