using MissionControl.Dashboard.Refresh;

namespace MissionControl.Dashboard.GreenCloud;

internal sealed class GreenCloudBandwidthFleetRefreshController(
    IGreenCloudBandwidthFleetClient client)
{
    private readonly RefreshGate refreshGate = new();

    private IReadOnlyList<GreenCloudBandwidthNodeResult> nodes = [];

    public IReadOnlyList<GreenCloudBandwidthNodeResult> CurrentNodes => nodes;

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
                    IReadOnlyList<GreenCloudBandwidthNodeResult> refreshed = await client.GetAllAsync(token);
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
                    RefreshWarning =
                        "Latest GreenCloud refresh failed: " +
                        exception.Message;
                }
                finally
                {
                    IsInitialLoading = false;
                }
            },
            cancellationToken);
    }

    private IReadOnlyList<GreenCloudBandwidthNodeResult>
        MergeWithPrevious(
            IReadOnlyList<GreenCloudBandwidthNodeResult> refreshed)
    {
        Dictionary<string, GreenCloudBandwidthNodeResult>
            previousByNode = nodes.ToDictionary(
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

                    if (!previousByNode.TryGetValue(node.NodeId, out GreenCloudBandwidthNodeResult? previous) ||
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