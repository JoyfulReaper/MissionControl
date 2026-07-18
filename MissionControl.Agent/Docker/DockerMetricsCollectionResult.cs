using MissionControl.Agent.Models;

namespace MissionControl.Agent.Docker;

public sealed record DockerMetricsCollectionResult(
    bool Succeeded,
    IReadOnlyList<ContainerMetric> Containers,
    string? Error);
