using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Atmos.Web.Pages;

public class IndexModel : PageModel
{
    /// <summary>Mirrors the reference app's ?zip= direct-link support (weather-server.ts:2252-2253).</summary>
    public IActionResult OnGet(string? zip)
    {
        if (!string.IsNullOrEmpty(zip))
        {
            return RedirectToPage("/Weather", new { zip });
        }

        return Page();
    }
}
