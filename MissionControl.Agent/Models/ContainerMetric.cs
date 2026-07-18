namespace MissionControl.Agent.Models;

public sealed record ContainerMetric(
    string Name,
    string Image,
    string State,
    long? MemoryUsageBytes,
    long? MemoryLimitBytes,
    double? MemoryPercent,
    double? CpuPercent,
    int? RestartCount);
