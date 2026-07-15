using MissionControl.Agent.Models;

namespace MissionControl.Agent.Publishing;

internal interface INodeSnapshotPublisher
{
    Task PublishAsync(
        NodeSnapshotEvent snapshot,
        CancellationToken cancellationToken);
}