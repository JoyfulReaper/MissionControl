using MissionControl.Dashboard.Agents;

namespace MissionControl.Dashboard.Refresh;

internal sealed class AgentFleetRefreshController(
    IAgentFleetClient client,
    TimeProvider timeProvider,
    TimeSpan staleAfter)
{
    private readonly RefreshGate refreshGate = new();

    private IReadOnlyList<AgentNodeResult> nodes = [];

    public IReadOnlyList<AgentNodeResult> CurrentNodes =>
        nodes.Select(ApplyFreshness).ToArray();

    public bool IsInitialLoading { get; private set; } = true;

    public bool IsRefreshing => refreshGate.IsRunning;

    public string? RefreshWarning { get; private set; }

    public async Task<bool> RefreshAsync(
        CancellationToken cancellationToken)
    {
        return await refreshGate.TryRunAsync(
            async token =>
            {
                try
                {
                    IReadOnlyList<AgentNodeResult> refreshed = await client.GetSnapshotsAsync(token);

                    nodes = MergeWithPrevious(refreshed);
                    RefreshWarning = null;
                }
                catch (OperationCanceledException)
                    when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                    when (exception is
                        HttpRequestException or
                        TaskCanceledException or
                        InvalidOperationException)
                {
                    RefreshWarning = $"Latest Agent fleet refresh failed: " + exception.Message;
                }
                finally
                {
                    IsInitialLoading = false;
                }
            },
            cancellationToken);
    }

    private IReadOnlyList<AgentNodeResult> MergeWithPrevious(
        IReadOnlyList<AgentNodeResult> refreshed)
    {
        Dictionary<string, AgentNodeResult> previousByNode =
            nodes.ToDictionary(node => node.NodeId, StringComparer.OrdinalIgnoreCase);

        return refreshed
            .Select(
                node =>
                {
                    if (node.Snapshot is not null)
                    {
                        return node;
                    }

                    if (!previousByNode.TryGetValue(
                            node.NodeId,
                            out AgentNodeResult? previous) ||
                        previous.Snapshot is null)
                    {
                        return node;
                    }

                    return node with
                    {
                        Snapshot = previous.Snapshot
                    };
                })
            .ToArray();
    }

    private AgentNodeResult ApplyFreshness(
        AgentNodeResult node)
    {
        if (node.Snapshot is null)
        {
            return node;
        }

        return node with
        {
            Snapshot = SnapshotFreshness.Apply(
                node.Snapshot,
                timeProvider.GetUtcNow(),
                staleAfter)
        };
    }
}