namespace MissionControl.Dashboard.Agent;

public sealed record AgentSnapshotItem(
    string Node,
    DateTimeOffset CapturedAt,
    long AgeSeconds,
    bool Stale,
    HostMetricItem? Host,
    bool? MissionControlPublishSucceeded,
    DateTimeOffset? LastMissionControlPublishAttemptAt,
    IReadOnlyList<AgentProtocolStatusItem> Protocols,
    IReadOnlyList<AgentContainerStatusItem> Containers,
    bool? DockerAvailable = null,
    string? DockerError = null);

public sealed record HostMetricItem(
    int LogicalProcessorCount,
    double? CpuPercent,
    long? MemoryTotalBytes,
    long? MemoryAvailableBytes);
