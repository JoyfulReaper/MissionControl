using System.Net.Http.Json;

namespace MissionControl.Client.Infrastructure;

public sealed class BandwidthUsageClient(
    HttpClient client) : IBandwidthUsageClient
{
    public async Task<BandwidthUsageSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await client.GetAsync("api/mobile/bandwidth", cancellationToken);

        response.EnsureSuccessStatusCode();

        BandwidthUsageSnapshot? snapshot =
            await response.Content.ReadFromJsonAsync<BandwidthUsageSnapshot>(cancellationToken);

        return snapshot ??
            throw new InvalidOperationException("The bandwidth API response was empty.");
    }
}