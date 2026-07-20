using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

namespace MissionControl.Dashboard.MobileApi;

public sealed class MobileApiAuthenticationHandler(
    IOptionsMonitor<MobileApiAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<MobileApiAuthenticationOptions>(
        options,
        logger,
        encoder)
{
    private const int Sha256HashLength = 32;

    protected override Task<AuthenticateResult>
        HandleAuthenticateAsync()
    {
        if (!Options.Enabled)
        {
            return Task.FromResult(
                AuthenticateResult.Fail(
                    "The Mobile API is disabled."));
        }

        if (!Request.Headers.TryGetValue(
                HeaderNames.Authorization,
                out var authorizationValues))
        {
            return Task.FromResult(
                AuthenticateResult.NoResult());
        }

        if (!AuthenticationHeaderValue.TryParse(
                authorizationValues.ToString(),
                out AuthenticationHeaderValue? authorization) ||
            !string.Equals(
                authorization.Scheme,
                "Bearer",
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(
                authorization.Parameter))
        {
            return Task.FromResult(
                AuthenticateResult.Fail(
                    "A valid Bearer token is required."));
        }

        byte[] configuredHash;

        try
        {
            configuredHash =
                Convert.FromBase64String(
                    Options.TokenHash);
        }
        catch (FormatException)
        {
            Logger.LogError(
                "Dashboard Mobile API TokenHash is not valid Base64.");

            return Task.FromResult(
                AuthenticateResult.Fail(
                    "The Mobile API is not configured correctly."));
        }

        if (configuredHash.Length != Sha256HashLength)
        {
            Logger.LogError(
                "Dashboard Mobile API TokenHash is not a SHA-256 hash.");

            return Task.FromResult(
                AuthenticateResult.Fail(
                    "The Mobile API is not configured correctly."));
        }

        byte[] suppliedHash =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    authorization.Parameter));

        if (!CryptographicOperations.FixedTimeEquals(
                configuredHash,
                suppliedHash))
        {
            return Task.FromResult(
                AuthenticateResult.Fail(
                    "The supplied Mobile API token is invalid."));
        }

        ClaimsIdentity identity =
            new(
                [
                    new Claim(
                        ClaimTypes.NameIdentifier,
                        "mission-control-mobile"),
                    new Claim(
                        ClaimTypes.Name,
                        "Mission Control Mobile")
                ],
                Scheme.Name);

        ClaimsPrincipal principal =
            new(identity);

        AuthenticationTicket ticket =
            new(
                principal,
                Scheme.Name);

        return Task.FromResult(
            AuthenticateResult.Success(ticket));
    }

    protected override async Task HandleChallengeAsync(
        AuthenticationProperties properties)
    {
        Response.StatusCode =
            StatusCodes.Status401Unauthorized;

        Response.Headers[
            HeaderNames.WWWAuthenticate] =
            "Bearer";

        await Response.WriteAsJsonAsync(
            new
            {
                title = "Unauthorized",
                status =
                    StatusCodes.Status401Unauthorized
            });
    }
}