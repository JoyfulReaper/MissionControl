extern alias AgentApp;

using AgentApp::MissionControl.Agent.Models;
using AgentApp::MissionControl.Agent.Publishing;
using Xunit;

namespace MissionControl.Tests;

public sealed class SnapshotPublicationGateTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void DockerAvailabilityTransitionIsOperationalChange(
        bool publishedAvailability,
        bool currentAvailability)
    {
        var gate = new SnapshotPublicationGate(TimeSpan.FromHours(1));
        DateTimeOffset publishedAt = DateTimeOffset.UtcNow;

        gate.MarkPublished(
            CreateSnapshot(
                dockerAvailable: publishedAvailability),
            publishedAt);

        Assert.True(
            gate.IsDue(
                CreateSnapshot(
                    dockerAvailable: currentAvailability),
                publishedAt.AddMinutes(1)));
    }

    [Fact]
    public void HostMetricChangesDoNotTriggerPublication()
    {
        var gate = new SnapshotPublicationGate(TimeSpan.FromHours(1));
        DateTimeOffset publishedAt = DateTimeOffset.UtcNow;

        gate.MarkPublished(
            CreateSnapshot(
                dockerAvailable: true,
                cpuPercent: 10),
            publishedAt);

        Assert.False(
            gate.IsDue(
                CreateSnapshot(
                    dockerAvailable: true,
                    cpuPercent: 80),
                publishedAt.AddMinutes(1)));
    }

    [Fact]
    public void DockerDiagnosticChangesDoNotTriggerPublication()
    {
        var gate = new SnapshotPublicationGate(TimeSpan.FromHours(1));
        DateTimeOffset publishedAt = DateTimeOffset.UtcNow;
        NodeSnapshotEvent published =
            CreateSnapshot(dockerAvailable: false) with
            {
                DockerError = "Docker metric collection timed out."
            };
        NodeSnapshotEvent current =
            CreateSnapshot(dockerAvailable: false) with
            {
                DockerError = "Docker metric collection is unavailable."
            };

        gate.MarkPublished(published, publishedAt);

        Assert.False(
            gate.IsDue(
                current,
                publishedAt.AddMinutes(1)));
    }

    private static NodeSnapshotEvent CreateSnapshot(
        bool dockerAvailable,
        double cpuPercent = 10)
    {
        return new NodeSnapshotEvent(
            Node: "node-1",
            CapturedAt: DateTimeOffset.UtcNow,
            Host: new HostMetric(
                LogicalProcessorCount: 4,
                CpuPercent: cpuPercent,
                MemoryTotalBytes: 1_000,
                MemoryAvailableBytes: 500),
            Protocols: [],
            Containers: [],
            DockerAvailable: dockerAvailable,
            DockerError: dockerAvailable
                ? null
                : "Docker metric collection is unavailable.");
    }
}
