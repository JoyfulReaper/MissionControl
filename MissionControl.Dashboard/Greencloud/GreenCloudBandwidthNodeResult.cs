using MissionControl.Client.Infrastructure;

namespace MissionControl.Dashboard.GreenCloud;

public sealed record GreenCloudBandwidthNodeResult(
    string NodeId,
    BandwidthUsageSnapshot? Snapshot,
    string? Error)
{
    public bool Succeeded =>
        Snapshot is not null &&
        Error is null;
}