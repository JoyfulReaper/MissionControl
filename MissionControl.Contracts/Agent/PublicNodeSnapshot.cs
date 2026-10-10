namespace MissionControl.Contracts.Agent;

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
    string? DockerError = null)
{
    public string? NodeId { get; init; }

    // Independent of the last attempt result; null means the Agent did not report this setting.
    public bool? MissionControlPublicationEnabled { get; init; }

    public DateTimeOffset? HostCapturedAt { get; init; }
}
