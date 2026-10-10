using MissionControl.Dashboard.Refresh;

namespace MissionControl.Dashboard.Bandwidth;

internal class BandwidthFleetRefreshController(
    IBandwidthFleetClient client,
    IEnumerable<string> nodeIds, string providerName)
{
    private readonly RefreshGate refreshGate = new();

    private IReadOnlyList<BandwidthNodeResult> nodes =
        nodeIds.Select(nodeId => new BandwidthNodeResult(
            nodeId, null, $"{providerName} bandwidth has not been refreshed yet."))
            .ToArray();
    public IReadOnlyList<BandwidthNodeResult> CurrentNodes =>
        Volatile.Read(ref nodes);

    public bool IsInitialLoading { get; private set; } = true;

    public bool IsRefreshing =>
        refreshGate.IsRunning;

    public string? RefreshWarning { get; private set; }

    public async Task<bool> RefreshAsync(
        CancellationToken cancellationToken)
    {
        return await refreshGate.TryRunAsync(
            async token =>
            {
                try
                {
                    IReadOnlyList<BandwidthNodeResult> refreshed = await client.GetAllAsync(token);
                    Volatile.Write(
                        ref nodes,
                        MergeWithPrevious(refreshed));

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
                        InvalidOperationException or
                        System.Text.Json.JsonException)
                {
                    RefreshWarning =
                        $"Latest {providerName} refresh failed.";

                    Volatile.Write(
                        ref nodes,
                        CurrentNodes
                            .Select(
                                node => node with
                                {
                                    Error = RefreshWarning
                                })
                            .ToArray());
                }
                finally
                {
                    IsInitialLoading = false;
                }
            },
            cancellationToken);
    }

    private IReadOnlyList<BandwidthNodeResult>
        MergeWithPrevious(
            IReadOnlyList<BandwidthNodeResult> refreshed)
    {
        Dictionary<string, BandwidthNodeResult>
            previousByNode = CurrentNodes.ToDictionary(
                node => node.NodeId,
                StringComparer.OrdinalIgnoreCase);

        return refreshed
            .Select(
                node =>
                {
                    if (node.Snapshot is not null)
                    {
                        return node;
                    }

                    if (!previousByNode.TryGetValue(node.NodeId, out BandwidthNodeResult? previous) ||
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
}
