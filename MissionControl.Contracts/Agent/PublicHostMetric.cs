namespace MissionControl.Contracts.Agent;

public sealed record PublicHostMetric(
    int LogicalProcessorCount,
    double? CpuPercent,
    long? MemoryTotalBytes,
    long? MemoryAvailableBytes);