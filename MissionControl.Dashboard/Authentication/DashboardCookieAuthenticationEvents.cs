using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using MissionControl.Dashboard.Security;

namespace MissionControl.Dashboard.Authentication;

public sealed class DashboardCookieAuthenticationEvents(
    IDashboardUserStore userStore) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(
        CookieValidatePrincipalContext context)
    {
        string? username =
            context.Principal?.FindFirst(DashboardAuthenticationDefaults.UsernameClaim)
            ?.Value;

        string? securityStamp =
            context.Principal?.FindFirst(DashboardAuthenticationDefaults.SecurityStampClaim)
            ?.Value;

        if (string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(securityStamp))
        {
            await RejectAsync(context);
            return;
        }

        string normalizedUsername = DashboardUsernameNormalizer.Normalize(username);

        DashboardUser? user =
            await userStore.FindByNormalizedUsernameAsync(
                normalizedUsername,
                context.HttpContext.RequestAborted);

        if (user is null ||
            !user.IsEnabled ||
            !string.Equals(user.SecurityStamp, securityStamp, StringComparison.Ordinal))
        {
            await RejectAsync(context);
        }
    }

    private static async Task RejectAsync(
        CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();

        await context.HttpContext.SignOutAsync(DashboardAuthenticationDefaults.Scheme);
    }
}