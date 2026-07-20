using System.Net;

namespace MissionControl.Mobile.Services;

public sealed class MobileApiConnectionClient(
    HttpClient httpClient)
{
    public async Task TestAsync(
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await httpClient.GetAsync(
                "api/mobile/ping",
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