using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MissionControl.Dashboard.Security;

namespace MissionControl.Dashboard.Pages;

[Authorize]
public sealed class LogoutModel : PageModel
{
    public IActionResult OnGet()
    {
        return NotFound();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync(
            DashboardAuthenticationDefaults.Scheme);

        return RedirectToPage("/Login");
    }
}