using MissionControl.Agent.Models;

namespace MissionControl.Agent.Storage;

internal interface INodeSnapshotStore
{
    Task SaveAsync(
        NodeSnapshotEvent snapshot,
        CancellationToken cancellationToken = default);

    Task RecordPublishResultAsync(
        string node,
        bool succeeded,
        DateTimeOffset attemptedAt,
        CancellationToken cancellationToken = default);

    Task<StoredNodeSnapshot?> GetAsync(
        string node,
        CancellationToken cancellationToken = default);
}
