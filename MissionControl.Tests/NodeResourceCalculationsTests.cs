extern alias DashboardApp;

using DashboardApp::MissionControl.Dashboard.Agent;
using DashboardApp::MissionControl.Dashboard.Components.Overview;
using Xunit;

namespace MissionControl.Tests;

public sealed class NodeResourceCalculationsTests
{
    private const long Gibibyte = 1024L * 1024 * 1024;

    [Fact]
    public void CalculatesOverviewValuesUsingByteBasedHostMetrics()
    {
        var host = new HostMetricItem(
            LogicalProcessorCount: 8,
            CpuPercent: 37.5,
            MemoryTotalBytes: 16 * Gibibyte,
            MemoryAvailableBytes: 6 * Gibibyte);

        Assert.Equal(
            37.5,
            NodeResourceCalculations.GetCpuPercent(host));
        Assert.True(
            NodeResourceCalculations.HasMemoryMetrics(host));
        Assert.Equal(
            10 * Gibibyte,
            NodeResourceCalculations.GetMemoryUsedBytes(host));
        Assert.Equal(
            62.5,
            NodeResourceCalculations.GetMemoryPercent(host));
        Assert.Equal(
            "10 GB",
            NodeResourceCalculations.FormatBytes(10 * Gibibyte));
        Assert.Equal(
            "16 GB",
            NodeResourceCalculations.FormatBytes(16 * Gibibyte));
        Assert.Equal(
            "6 GB",
            NodeResourceCalculations.FormatBytes(6 * Gibibyte));
    }

    [Fact]
    public void PreservesNullCpuWithoutHidingValidMemory()
    {
        var host = new HostMetricItem(
            LogicalProcessorCount: 8,
            CpuPercent: null,
            MemoryTotalBytes: 16 * Gibibyte,
            MemoryAvailableBytes: 6 * Gibibyte);

        Assert.Null(
            NodeResourceCalculations.GetCpuPercent(host));
        Assert.True(
            NodeResourceCalculations.HasMemoryMetrics(host));
        Assert.Equal(
            10 * Gibibyte,
            NodeResourceCalculations.GetMemoryUsedBytes(host));
    }

    [Fact]
    public void TreatsNullMemoryAsUnavailable()
    {
        var host = new HostMetricItem(
            LogicalProcessorCount: 8,
            CpuPercent: 10,
            MemoryTotalBytes: null,
            MemoryAvailableBytes: null);

        Assert.False(
            NodeResourceCalculations.HasMemoryMetrics(host));
        Assert.Equal(
            0,
            NodeResourceCalculations.GetMemoryUsedBytes(host));
        Assert.Equal(
            0,
            NodeResourceCalculations.GetMemoryPercent(host));
    }

    [Fact]
    public void TreatsZeroTotalMemoryAsUnavailable()
    {
        var host = new HostMetricItem(
            LogicalProcessorCount: 8,
            CpuPercent: 10,
            MemoryTotalBytes: 0,
            MemoryAvailableBytes: 0);

        Assert.False(
            NodeResourceCalculations.HasMemoryMetrics(host));
        Assert.Equal(
            0,
            NodeResourceCalculations.GetMemoryPercent(host));
    }

    [Fact]
    public void ClampsMalformedAvailableMemoryToZeroUsedMemory()
    {
        var host = new HostMetricItem(
            LogicalProcessorCount: 8,
            CpuPercent: 10,
            MemoryTotalBytes: 6 * Gibibyte,
            MemoryAvailableBytes: 16 * Gibibyte);

        Assert.True(
            NodeResourceCalculations.HasMemoryMetrics(host));
        Assert.Equal(
            0,
            NodeResourceCalculations.GetMemoryUsedBytes(host));
        Assert.Equal(
            0,
            NodeResourceCalculations.GetMemoryPercent(host));
    }

    [Fact]
    public void MissingHostProducesNoResourceMetrics()
    {
        Assert.Null(
            NodeResourceCalculations.GetCpuPercent(null));
        Assert.False(
            NodeResourceCalculations.HasMemoryMetrics(null));
        Assert.Equal(
            0,
            NodeResourceCalculations.GetMemoryUsedBytes(null));
        Assert.Equal(
            0,
            NodeResourceCalculations.GetMemoryPercent(null));
        Assert.False(
            NodeResourceCalculations.HasResourceMetrics(null));
    }

    [Fact]
    public void NonNullHostWithoutCpuOrMemoryHasNoResourceMetrics()
    {
        var host = new HostMetricItem(
            LogicalProcessorCount: 8,
            CpuPercent: null,
            MemoryTotalBytes: null,
            MemoryAvailableBytes: null);

        Assert.False(
            NodeResourceCalculations.HasResourceMetrics(host));
        Assert.Equal(
            "Host resource metrics are unavailable for this snapshot.",
            NodeResourceCalculations.UnavailableMessage);
    }
}
