using System.Net;

namespace MissionControl.Dashboard.Agent;

public sealed class AgentSnapshotClient(HttpClient client)
    : IAgentSnapshotClient
{
    public async Task<AgentSnapshotItem> GetSnapshotAsync(
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

        AgentSnapshotItem? snapshot =
            await response.Content
                .ReadFromJsonAsync<AgentSnapshotItem>(
                    cancellationToken);

        return snapshot
            ?? throw new InvalidOperationException(
                "The Agent snapshot response was empty.");
    }
}