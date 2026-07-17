namespace MissionControl.Dashboard.Agent;

public interface IAgentSnapshotClient
{
    Task<AgentSnapshotItem> GetSnapshotAsync(
        CancellationToken cancellationToken = default);
}