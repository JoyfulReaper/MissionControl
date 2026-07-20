namespace MissionControl.Contracts.Agent;

public sealed record PublicContainerStatus(
    string Name,
    string State,
    long? MemoryUsageBytes,
    long? MemoryLimitBytes,
    double? MemoryPercent,
    double? CpuPercent,
    int? RestartCount,
    string? Image = null);