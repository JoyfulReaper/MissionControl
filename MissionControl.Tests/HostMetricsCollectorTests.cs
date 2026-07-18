extern alias AgentApp;

using AgentApp::MissionControl.Agent.Host;
using Xunit;

namespace MissionControl.Tests;

public sealed class HostMetricsCollectorTests
{
    [Fact]
    public void CalculateCpuPercentUsesAggregateJiffyDeltas()
    {
        var first =
            new HostMetricsCollector.CpuSample(
                Total: 1_000,
                Idle: 700);
        var second =
            new HostMetricsCollector.CpuSample(
                Total: 1_200,
                Idle: 820);

        double result =
            HostMetricsCollector.CalculateCpuPercent(
                first,
                second);

        Assert.Equal(40, result);
    }

    [Fact]
    public void ParseCpuSampleIncludesIoWaitAsIdle()
    {
        HostMetricsCollector.CpuSample result =
            HostMetricsCollector.ParseCpuSample(
                "cpu  100 20 30 400 50 10 5 1\ncpu0 1 2 3 4\n");

        Assert.Equal((ulong)616, result.Total);
        Assert.Equal((ulong)450, result.Idle);
    }

    [Fact]
    public void CountLogicalProcessorsCountsNumberedCpuRows()
    {
        int result =
            HostMetricsCollector.CountLogicalProcessors(
                "cpu  1 2 3 4\ncpu0 1 2 3 4\ncpu1 1 2 3 4\nintr 10\n");

        Assert.Equal(2, result);
    }

    [Fact]
    public void ParseMemoryConvertsKilobytesToBytes()
    {
        (long totalBytes, long availableBytes) =
            HostMetricsCollector.ParseMemory(
                "MemTotal:       4096 kB\nMemFree: 512 kB\nMemAvailable: 1536 kB\n");

        Assert.Equal(4_194_304, totalBytes);
        Assert.Equal(1_572_864, availableBytes);
    }
}
