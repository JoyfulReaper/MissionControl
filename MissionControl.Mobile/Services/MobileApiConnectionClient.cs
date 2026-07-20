using System.Net;
using System.Net.Http.Headers;

namespace MissionControl.Mobile.Services;

public sealed class MobileApiConnectionClient(
    HttpClient httpClient)
{
    public async Task TestAsync(
        string? candidateToken = null,
        CancellationToken cancellationToken = default)
    {
        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                "api/mobile/ping");

        if (!string.IsNullOrWhiteSpace(candidateToken))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    candidateToken.Trim());
        }

        using HttpResponseMessage response =
            await httpClient.SendAsync(
                request,
                cancellationToken);

        if (response.StatusCode ==
            HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException(
                "The Dashboard rejected the Mobile API token.");
        }

        response.EnsureSuccessStatusCode();
    }
}