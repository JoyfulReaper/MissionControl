using MissionControl.Contracts.Agent;

namespace MissionControl.Client.Infrastructure;

public sealed record HostNodeSnapshot(
    string NodeId,
    string DisplayName,
    PublicNodeSnapshot? AgentSnapshot,
    string? AgentError,
    bool BandwidthConfigured,
    BandwidthUsageSnapshot? Bandwidth,
    string? BandwidthError)
{
    // Defaults to the existing Dashboard threshold when reading an older server response.
    public int SnapshotStaleAfterSeconds { get; init; } = 120;
}
