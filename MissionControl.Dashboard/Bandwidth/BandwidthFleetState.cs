namespace MissionControl.Dashboard.Bandwidth;

// A read-only view over independently refreshed provider caches. Requests never poll providers.
internal sealed class BandwidthFleetState(IEnumerable<BandwidthFleetRefreshController> providers)
{
    private readonly BandwidthFleetRefreshController[] providers = providers.ToArray();

    public IReadOnlyList<BandwidthNodeResult> CurrentNodes =>
        providers.SelectMany(provider => provider.CurrentNodes).ToArray();

    public bool IsInitialLoading(string nodeId) =>
        providers.Any(provider => provider.IsInitialLoading && provider.CurrentNodes.Any(
            node => string.Equals(node.NodeId, nodeId, StringComparison.OrdinalIgnoreCase)));
}
