namespace MissionControl.Contracts.Agent;

public sealed record PublicHostMetric(
    int LogicalProcessorCount,
    double? CpuPercent,
    long? MemoryTotalBytes,
    long? MemoryAvailableBytes)
{
    public double? LoadAverage1Minute { get; init; }

    public double? LoadAverage5Minutes { get; init; }

    public double? LoadAverage15Minutes { get; init; }
}
