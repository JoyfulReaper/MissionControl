namespace MissionControl.Dashboard.Agent;

public sealed record AgentContainerStatusItem(
    string Name,
    string State,
    long? MemoryUsageBytes,
    long? MemoryLimitBytes,
    double? MemoryPercent,
    double? CpuPercent,
    int? RestartCount,
    string? Image = null);
