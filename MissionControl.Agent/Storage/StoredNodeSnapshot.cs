using MissionControl.Agent.Models;

namespace MissionControl.Agent.Storage;

internal sealed record StoredNodeSnapshot(
    NodeSnapshotEvent Snapshot,
    bool? PublishSucceeded,
    DateTimeOffset? LastPublishAttemptAt,
    DateTimeOffset UpdatedAt);
