using MissionControl.Contracts.Agent;

namespace MissionControl.Dashboard.Refresh;

internal static class SnapshotFreshness
{
    public static PublicNodeSnapshot Apply(
        PublicNodeSnapshot snapshot,
        DateTimeOffset utcNow,
        TimeSpan staleAfter)
        => MissionControl.Client.Agent.AgentSnapshotFreshness.Apply(snapshot, utcNow, staleAfter);
}
