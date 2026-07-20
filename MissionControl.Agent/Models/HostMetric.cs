namespace MissionControl.Agent.Models;

public sealed record HostMetric(
    int LogicalProcessorCount,
    double? CpuPercent,
    long? MemoryTotalBytes,
    long? MemoryAvailableBytes)
{
    public double? LoadAverage1Minute { get; init; }

    public double? LoadAverage5Minutes { get; init; }

    public double? LoadAverage15Minutes { get; init; }
}
