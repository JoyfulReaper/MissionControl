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

    [Theory]
    [InlineData("running", "exited")]
    [InlineData("exited", "running")]
    [InlineData("running", "stopped")]
    [InlineData("created", "restarting")]
    public void ContainerStateTransitionIsOperationalChange(
        string publishedState,
        string currentState)
    {
        AssertOperationalChange(
            [CreateContainer("api", publishedState)],
            [CreateContainer("api", currentState)]);
    }

    [Fact]
    public void ContainerAppearanceIsOperationalChange()
    {
        AssertOperationalChange(
            [],
            [CreateContainer("api", "running")]);
    }

    [Fact]
    public void ContainerDisappearanceIsOperationalChange()
    {
        AssertOperationalChange(
            [CreateContainer("api", "running")],
            []);
    }

    [Fact]
    public void RestartCountChangeIsOperationalChange()
    {
        AssertOperationalChange(
            [CreateContainer("api", "running", restartCount: 1)],
            [CreateContainer("api", "running", restartCount: 2)]);
    }

    [Fact]
    public void ContainerResourceChangesDoNotTriggerPublication()
    {
        var gate = new SnapshotPublicationGate(TimeSpan.FromHours(1));
        DateTimeOffset publishedAt = DateTimeOffset.UtcNow;
        NodeSnapshotEvent published = CreateSnapshot(
            dockerAvailable: true,
            containers:
            [
                CreateContainer("api", "running")
            ]);
        NodeSnapshotEvent current = CreateSnapshot(
            dockerAvailable: true,
            containers:
            [
                CreateContainer("api", "running") with
                {
                    MemoryUsageBytes = 900,
                    MemoryPercent = 90,
                    CpuPercent = 75
                }
            ]);

        gate.MarkPublished(published, publishedAt);

        Assert.False(
            gate.IsDue(
                current,
                publishedAt.AddMinutes(1)));
    }

    [Fact]
    public void ContainerOrderingDoesNotTriggerPublication()
    {
        var gate = new SnapshotPublicationGate(TimeSpan.FromHours(1));
        DateTimeOffset publishedAt = DateTimeOffset.UtcNow;
        NodeSnapshotEvent published = CreateSnapshot(
            dockerAvailable: true,
            containers:
            [
                CreateContainer("api", "running"),
                CreateContainer("worker", "exited")
            ]);
        NodeSnapshotEvent reordered = CreateSnapshot(
            dockerAvailable: true,
            containers:
            [
                CreateContainer("worker", "exited"),
                CreateContainer("api", "running")
            ]);

        gate.MarkPublished(published, publishedAt);

        Assert.False(
            gate.IsDue(
                reordered,
                publishedAt.AddMinutes(1)));
    }

    private static void AssertOperationalChange(
        IReadOnlyList<ContainerMetric> publishedContainers,
        IReadOnlyList<ContainerMetric> currentContainers)
    {
        var gate = new SnapshotPublicationGate(TimeSpan.FromHours(1));
        DateTimeOffset publishedAt = DateTimeOffset.UtcNow;

        gate.MarkPublished(
            CreateSnapshot(
                dockerAvailable: true,
                containers: publishedContainers),
            publishedAt);

        Assert.True(
            gate.IsDue(
                CreateSnapshot(
                    dockerAvailable: true,
                    containers: currentContainers),
                publishedAt.AddMinutes(1)));
    }

    private static ContainerMetric CreateContainer(
        string name,
        string state,
        int? restartCount = 1)
    {
        return new ContainerMetric(
            Name: name,
            Image: $"missioncontrol/{name}:latest",
            State: state,
            MemoryUsageBytes: 100,
            MemoryLimitBytes: 1_000,
            MemoryPercent: 10,
            CpuPercent: 5,
            RestartCount: restartCount);
    }

    private static NodeSnapshotEvent CreateSnapshot(
        bool dockerAvailable,
        double cpuPercent = 10,
        IReadOnlyList<ContainerMetric>? containers = null)
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
            Containers: containers ?? [],
            DockerAvailable: dockerAvailable,
            DockerError: dockerAvailable
                ? null
                : "Docker metric collection is unavailable.");
    }
}
