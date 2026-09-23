using Microsoft.Extensions.Options;
using MissionControl.Client.Agent;
using MissionControl.Contracts.Agent;

namespace MissionControl.Dashboard.Agents;

public sealed class AgentNodeSnapshotClient(
    IHttpClientFactory httpClientFactory,
    IOptions<AgentNodesOptions> options)
    : IAgentFleetClient
{
    internal const string HttpClientName = "MissionControl.AgentNodes";

    private readonly AgentNodesOptions _options = options.Value;

    public async Task<IReadOnlyList<AgentNodeResult>>
        GetSnapshotsAsync(CancellationToken cancellationToken = default)
    {
        Task<AgentNodeResult>[] tasks =
            _options.Nodes
                .Select(node => GetSnapshotAsync(node, cancellationToken))
                .ToArray();

        return await Task.WhenAll(tasks);
    }

    private async Task<AgentNodeResult> GetSnapshotAsync(
        AgentNodeOptions node,
        CancellationToken cancellationToken)
    {
        try
        {
            HttpClient httpClient = httpClientFactory.CreateClient(HttpClientName);
            httpClient.BaseAddress = CreateBaseUri(node.BaseUrl);

            var client = new AgentSnapshotClient(httpClient);

            PublicNodeSnapshot snapshot = await client.GetSnapshotAsync(cancellationToken);

            return new AgentNodeResult(
                node.NodeId,
                node.DisplayName,
                snapshot,
                Error: null);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is
                HttpRequestException or
                TaskCanceledException or
                InvalidOperationException)
        {
            return new AgentNodeResult(
                node.NodeId,
                node.DisplayName,
                Snapshot: null,
                Error: exception.Message);
        }
    }

    private static Uri CreateBaseUri(string value)
    {
        string normalized =
            value.EndsWith(
                "/",
                StringComparison.Ordinal)
                ? value
                : $"{value}/";

        return new Uri(
            normalized,
            UriKind.Absolute);
    }
}