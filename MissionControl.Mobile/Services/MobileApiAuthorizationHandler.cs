using System.Net.Http.Headers;

namespace MissionControl.Mobile.Services;

public sealed class MobileApiAuthorizationHandler(
    MobileApiCredentialStore credentialStore)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage>
        SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is null)
        {
            string? token =
                await credentialStore.GetTokenAsync();

            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue(
                        "Bearer",
                        token);
            }
        }

        return await base.SendAsync(
            request,
            cancellationToken);
    }
}