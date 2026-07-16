namespace MissionControl.Agent.Contracts;

public sealed record PublicNodeSnapshot(
    string Node,
    DateTimeOffset CapturedAt,
    long AgeSeconds,
    bool Stale,
    bool? MissionControlPublishSucceeded,
    DateTimeOffset? LastMissionControlPublishAttemptAt,
    IReadOnlyList<PublicProtocolStatus> Protocols,
    IReadOnlyList<PublicContainerStatus> Containers);