using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MissionControl.Dashboard.Authentication;
using MissionControl.Dashboard.Events;
using MissionControl.Dashboard.Security;
using System.Globalization;
using System.Security.Claims;

namespace MissionControl.Dashboard.Pages;

[AllowAnonymous]
public sealed class LoginModel(
    DashboardPasswordAuthenticationService
        authenticationService,
    DashboardLoginEventPublisher
        loginEventPublisher)
    : PageModel
{
    [BindProperty]
    public string Username { get; set; } =
        string.Empty;

    [BindProperty]
    public string Password { get; set; } =
        string.Empty;

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public bool InvalidCredentials { get; private set; }

    public IActionResult OnGet()
    {
        ReturnUrl = GetSafeReturnUrl(
            ReturnUrl);

        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(ReturnUrl);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ReturnUrl = GetSafeReturnUrl(
            ReturnUrl);

        DashboardPasswordAuthenticationResult result =
            await authenticationService.AuthenticateAsync(
                Username,
                Password,
                HttpContext.RequestAborted);

        if (!result.Succeeded ||
            result.User is null)
        {
            InvalidCredentials = true;
            Password = string.Empty;

            return Page();
        }

        DashboardUser user = result.User;

        Claim[] claims =
        [
            new(
                ClaimTypes.NameIdentifier,
                user.Id.ToString(
                    CultureInfo.InvariantCulture)),

            new(
                ClaimTypes.Name,
                user.DisplayName),

            new(
                DashboardAuthenticationDefaults
                    .UsernameClaim,
                user.Username),

            new(
                DashboardAuthenticationDefaults
                    .SecurityStampClaim,
                user.SecurityStamp),

            new(
                DashboardAuthenticationDefaults
                    .PermissionClaim,
                DashboardAuthenticationDefaults
                    .RawIpPermission)
        ];

        var identity =
            new ClaimsIdentity(
                claims,
                DashboardAuthenticationDefaults.Scheme);

        var principal =
            new ClaimsPrincipal(identity);

        var properties =
            new AuthenticationProperties
            {
                AllowRefresh = true,

                // Closing the browser ends the session.
                IsPersistent = false
            };

        await HttpContext.SignInAsync(
            DashboardAuthenticationDefaults.Scheme,
            principal,
            properties);

        await loginEventPublisher.TryPublishAsync(
            user,
            HttpContext.Connection
                .RemoteIpAddress?
                .ToString(),
            HttpContext.RequestAborted);

        return LocalRedirect(ReturnUrl);
    }

    private string GetSafeReturnUrl(
        string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) ||
            !Url.IsLocalUrl(returnUrl) ||
            returnUrl.StartsWith(
                "/login",
                StringComparison.OrdinalIgnoreCase))
        {
            return Url.Content("~/");
        }

        return returnUrl;
    }
}