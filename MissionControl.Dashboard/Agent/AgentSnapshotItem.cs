namespace MissionControl.Dashboard.Agent;

public sealed record AgentSnapshotItem(
    string Node,
    DateTimeOffset CapturedAt,
    long AgeSeconds,
    bool Stale,
    bool? MissionControlPublishSucceeded,
    DateTimeOffset? LastMissionControlPublishAttemptAt,
    IReadOnlyList<AgentProtocolStatusItem> Protocols,
    IReadOnlyList<AgentContainerStatusItem> Containers);