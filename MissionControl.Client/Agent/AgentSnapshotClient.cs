using MissionControl.Contracts.Agent;
using System.Net;
using System.Net.Http.Json;

namespace MissionControl.Client.Agent;

public sealed class AgentSnapshotClient(HttpClient client)
    : IAgentSnapshotClient
{
    public async Task<PublicNodeSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await client.GetAsync(
                "api/snapshot",
                cancellationToken);

        if (response.StatusCode ==
            HttpStatusCode.ServiceUnavailable)
        {
            throw new InvalidOperationException(
                "No Agent snapshot is currently available.");
        }

        response.EnsureSuccessStatusCode();

        PublicNodeSnapshot? snapshot =
            await response.Content
                .ReadFromJsonAsync<PublicNodeSnapshot>(
                    cancellationToken);

        return snapshot
            ?? throw new InvalidOperationException(
                "The Agent snapshot response was empty.");
    }
}