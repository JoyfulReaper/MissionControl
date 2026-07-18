extern alias AgentApp;

using AgentApp::MissionControl.Agent.Models;
using AgentApp::MissionControl.Agent.Publishing;
using Xunit;

namespace MissionControl.Tests;

public sealed class SnapshotPublicationGateTests
{
    [Fact]
    public void FirstSnapshotIsDue()
    {
        var gate = new SnapshotPublicationGate(
            TimeSpan.FromMinutes(15));

        Assert.True(
            gate.IsDue(
                CreateSnapshot(dockerAvailable: true),
                DateTimeOffset.UtcNow));
    }

    [Fact]
    public void UnchangedSnapshotIsSuppressedBeforeHeartbeatAndDueAtHeartbeat()
    {
        var gate = new SnapshotPublicationGate(
            TimeSpan.FromMinutes(3));
        DateTimeOffset publishedAt = DateTimeOffset.UtcNow;
        NodeSnapshotEvent snapshot =
            CreateSnapshot(dockerAvailable: true);

        gate.MarkPublished(snapshot, publishedAt);

        Assert.False(
            gate.IsDue(
                snapshot,
                publishedAt.AddMinutes(3).AddTicks(-1)));
        Assert.True(
            gate.IsDue(
                snapshot,
                publishedAt.AddMinutes(3)));
    }

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
    public void CpuAndMemoryChangesDoNotTriggerPublication()
    {
        var gate = new SnapshotPublicationGate(TimeSpan.FromHours(1));
        DateTimeOffset publishedAt = DateTimeOffset.UtcNow;

        gate.MarkPublished(
            CreateSnapshot(
                dockerAvailable: true,
                cpuPercent: 10,
                memoryAvailableBytes: 500),
            publishedAt);

        Assert.False(
            gate.IsDue(
                CreateSnapshot(
                    dockerAvailable: true,
                    cpuPercent: 80,
                    memoryAvailableBytes: 100),
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

    [Fact]
    public void ProtocolEndpointChangeIsOperationalChange()
    {
        var gate = new SnapshotPublicationGate(TimeSpan.FromHours(1));
        DateTimeOffset publishedAt = DateTimeOffset.UtcNow;

        gate.MarkPublished(
            CreateSnapshot(
                dockerAvailable: true,
                protocols:
                [
                    CreateProtocol("old.internal:7")
                ]),
            publishedAt);

        Assert.True(
            gate.IsDue(
                CreateSnapshot(
                    dockerAvailable: true,
                    protocols:
                    [
                        CreateProtocol("new.internal:7")
                    ]),
                publishedAt.AddMinutes(1)));
    }

    [Fact]
    public void ProtocolErrorWordingAndDurationDoNotTriggerPublication()
    {
        var gate = new SnapshotPublicationGate(TimeSpan.FromHours(1));
        DateTimeOffset publishedAt = DateTimeOffset.UtcNow;

        gate.MarkPublished(
            CreateSnapshot(
                dockerAvailable: true,
                protocols:
                [
                    CreateProtocol(
                        "api.internal:7",
                        succeeded: false,
                        duration: 10,
                        error: "Connection refused")
                ]),
            publishedAt);

        Assert.False(
            gate.IsDue(
                CreateSnapshot(
                    dockerAvailable: true,
                    protocols:
                    [
                        CreateProtocol(
                            "api.internal:7",
                            succeeded: false,
                            duration: 999,
                            error: "Connection was refused by the host")
                    ]),
                publishedAt.AddMinutes(1)));
    }

    [Fact]
    public void ContainerImageChangeIsOperationalChange()
    {
        var gate = new SnapshotPublicationGate(TimeSpan.FromHours(1));
        DateTimeOffset publishedAt = DateTimeOffset.UtcNow;
        ContainerMetric published =
            CreateContainer("api", "running") with
            {
                Image = "missioncontrol/api:1"
            };
        ContainerMetric current = published with
        {
            Image = "missioncontrol/api:2"
        };

        gate.MarkPublished(
            CreateSnapshot(
                dockerAvailable: true,
                containers: [published]),
            publishedAt);

        Assert.True(
            gate.IsDue(
                CreateSnapshot(
                    dockerAvailable: true,
                    containers: [current]),
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

    private static ProtocolProbeResult CreateProtocol(
        string endpoint,
        bool succeeded = true,
        long duration = 10,
        string? error = null)
    {
        return new ProtocolProbeResult(
            Service: "echo",
            Endpoint: endpoint,
            Succeeded: succeeded,
            DurationMilliseconds: duration,
            Error: error);
    }

    private static NodeSnapshotEvent CreateSnapshot(
        bool dockerAvailable,
        double cpuPercent = 10,
        long memoryAvailableBytes = 500,
        IReadOnlyList<ContainerMetric>? containers = null,
        IReadOnlyList<ProtocolProbeResult>? protocols = null)
    {
        return new NodeSnapshotEvent(
            Node: "node-1",
            CapturedAt: DateTimeOffset.UtcNow,
            Host: new HostMetric(
                LogicalProcessorCount: 4,
                CpuPercent: cpuPercent,
                MemoryTotalBytes: 1_000,
                MemoryAvailableBytes: memoryAvailableBytes),
            Protocols: protocols ?? [],
            Containers: containers ?? [],
            DockerAvailable: dockerAvailable,
            DockerError: dockerAvailable
                ? null
                : "Docker metric collection is unavailable.");
    }
}
