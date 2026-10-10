using MissionControl.Client.Infrastructure;

namespace MissionControl.Dashboard.Bandwidth;

public sealed record BandwidthNodeResult(
    string NodeId,
    BandwidthUsageSnapshot? Snapshot,
    string? Error)
{
    public bool Succeeded =>
        Snapshot is not null &&
        Error is null;
}
