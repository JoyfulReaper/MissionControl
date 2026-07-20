using MissionControl.Contracts.Agent;

namespace MissionControl.Client.Agent;

public interface IAgentSnapshotClient
{
    Task<PublicNodeSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default);
}