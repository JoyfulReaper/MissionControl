using MissionControl.Contracts.Agent;

namespace MissionControl.Dashboard.Agents;

public sealed record AgentNodeResult(
    string NodeId,
    string DisplayName,
    PublicNodeSnapshot? Snapshot,
    string? Error)
{
    public bool Succeeded => Snapshot is not null;
}