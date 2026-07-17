namespace MissionControl.Agent.Models;

public sealed record HostMetric(
    int LogicalProcessorCount,
    double? CpuPercent,
    long? MemoryTotalBytes,
    long? MemoryAvailableBytes);
