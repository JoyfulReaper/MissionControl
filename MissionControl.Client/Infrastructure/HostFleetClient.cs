using System.Net.Http.Json;

namespace MissionControl.Client.Infrastructure;

public sealed class HostFleetClient(
    HttpClient client) : IHostFleetClient
{
    public async Task<IReadOnlyList<HostNodeSnapshot>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await client.GetAsync(
                "api/mobile/hosts",
                cancellationToken);

        response.EnsureSuccessStatusCode();

        HostNodeSnapshot[]? hosts =
            await response.Content
                .ReadFromJsonAsync<HostNodeSnapshot[]>(
                    cancellationToken);

        return hosts ??
            throw new InvalidOperationException(
                "The hosts API response was empty.");
    }
}
