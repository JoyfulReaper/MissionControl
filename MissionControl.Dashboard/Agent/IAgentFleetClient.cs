namespace MissionControl.Dashboard.Agents;

public interface IAgentFleetClient
{
    Task<IReadOnlyList<AgentNodeResult>> GetSnapshotsAsync(
        CancellationToken cancellationToken = default);
}