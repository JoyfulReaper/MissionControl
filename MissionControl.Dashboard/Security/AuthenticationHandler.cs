using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace MissionControl.Dashboard.Security;

public sealed class DashboardAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IHostEnvironment environment)
    : AuthenticationHandler<AuthenticationSchemeOptions>(
        options,
        logger,
        encoder)
{
    protected override Task<AuthenticateResult>
        HandleAuthenticateAsync()
    {
        if (!environment.IsDevelopment())
        {
            return Task.FromResult(
                AuthenticateResult.Fail(
                    "Production dashboard authentication is not configured."));
        }

        Claim[] claims =
        [
            new(
                ClaimTypes.NameIdentifier,
                "local-development"),

            new(
                ClaimTypes.Name,
                "Kyle"),

            new(
                ClaimTypes.Email,
                "local-development"),

            new(
                "permission",
                "events.raw-ip")
        ];

        var identity = new ClaimsIdentity(
            claims,
            Scheme.Name);

        var principal = new ClaimsPrincipal(identity);

        var ticket = new AuthenticationTicket(
            principal,
            Scheme.Name);

        return Task.FromResult(
            AuthenticateResult.Success(ticket));
    }
}