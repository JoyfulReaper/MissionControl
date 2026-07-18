namespace MissionControl.Agent.Contracts;

public sealed record PublicHostMetric(
    int LogicalProcessorCount,
    double? CpuPercent,
    long? MemoryTotalBytes,
    long? MemoryAvailableBytes);
