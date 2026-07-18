namespace MissionControl.Agent.Contracts;

public sealed record PublicNodeSnapshot(
    string Node,
    DateTimeOffset CapturedAt,
    long AgeSeconds,
    bool Stale,
    PublicHostMetric? Host,
    bool? MissionControlPublishSucceeded,
    DateTimeOffset? LastMissionControlPublishAttemptAt,
    IReadOnlyList<PublicProtocolStatus> Protocols,
    IReadOnlyList<PublicContainerStatus> Containers,
    bool? DockerAvailable = null,
    string? DockerError = null);
