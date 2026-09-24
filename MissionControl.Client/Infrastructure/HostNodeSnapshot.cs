using MissionControl.Contracts.Agent;

namespace MissionControl.Client.Infrastructure;

public sealed record HostNodeSnapshot(
    string NodeId,
    string DisplayName,
    PublicNodeSnapshot? AgentSnapshot,
    string? AgentError,
    bool BandwidthConfigured,
    BandwidthUsageSnapshot? Bandwidth,
    string? BandwidthError);
