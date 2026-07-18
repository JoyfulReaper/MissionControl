extern alias AgentApp;
extern alias DashboardApp;

using AgentApp::MissionControl.Agent;
using AgentApp::MissionControl.Agent.Docker;
using AgentApp::MissionControl.Agent.Endpoints;
using AgentApp::MissionControl.Agent.Host;
using AgentApp::MissionControl.Agent.Models;
using AgentApp::MissionControl.Agent.Protocols;
using AgentApp::MissionControl.Agent.Publishing;
using AgentApp::MissionControl.Agent.Storage;
using DashboardApp::MissionControl.Dashboard.Agent;
using DashboardApp::MissionControl.Dashboard.Components.Overview;
using JoyfulReaperLib.MissionControl;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using Xunit;

namespace MissionControl.Tests;

public sealed class AgentWorkerDockerFailureTests
{
    [Fact]
    public async Task CollectorExceptionStillPersistsHostAndProtocolSnapshot()
    {
        var store = new RecordingSnapshotStore();
        AgentWorker worker = CreateWorker(
            new ThrowingDockerMetricsCollector(),
            store);

        await worker.ExecuteIterationAsync(
            CreateOptions(),
            CancellationToken.None);

        NodeSnapshotEvent snapshot = Assert.IsType<NodeSnapshotEvent>(
            store.SavedSnapshot);
        Assert.False(snapshot.DockerAvailable);
        Assert.Equal(
            "Docker metric collection is unavailable.",
            snapshot.DockerError);
        Assert.Empty(snapshot.Containers);
        Assert.NotNull(snapshot.Host);
        Assert.Equal(4, snapshot.Host.LogicalProcessorCount);
        Assert.Equal(25, snapshot.Host.CpuPercent);
        Assert.Equal(1_000, snapshot.Host.MemoryTotalBytes);
        Assert.Equal(500, snapshot.Host.MemoryAvailableBytes);
        ProtocolProbeResult protocol = Assert.Single(snapshot.Protocols);
        Assert.True(protocol.Succeeded);
        Assert.Equal("health", protocol.Service);
        Assert.True(store.PublishWasAfterSave);
    }

    [Fact]
    public async Task SuccessfulEmptyCollectionIsDistinctFromFailure()
    {
        var store = new RecordingSnapshotStore();
        AgentWorker worker = CreateWorker(
            new SuccessfulDockerMetricsCollector(),
            store);

        await worker.ExecuteIterationAsync(
            CreateOptions(),
            CancellationToken.None);

        NodeSnapshotEvent snapshot = Assert.IsType<NodeSnapshotEvent>(
            store.SavedSnapshot);
        Assert.True(snapshot.DockerAvailable);
        Assert.Null(snapshot.DockerError);
        Assert.Empty(snapshot.Containers);
    }

    [Fact]
    public async Task ControlledCollectionCyclePreservesEveryCollectorResult()
    {
        await using var fixture =
            await AgentSnapshotStoreFixture.CreateAsync();
        var expectedHost = new HostMetric(
            LogicalProcessorCount: 12,
            CpuPercent: 37.5,
            MemoryTotalBytes: 17_179_869_184,
            MemoryAvailableBytes: 6_442_450_944);
        var expectedContainer = new ContainerMetric(
            Name: "missioncontrol-agent",
            Image: "missioncontrol/agent:3.0",
            State: "running",
            MemoryUsageBytes: 987_654_321,
            MemoryLimitBytes: 2_147_483_648,
            MemoryPercent: 45.99,
            CpuPercent: 12.75,
            RestartCount: 3);
        var hostCollector =
            new TrackingHostMetricsCollector(expectedHost);
        var dockerCollector =
            new TrackingDockerMetricsCollector([expectedContainer]);
        var protocolProbe = new TrackingProtocolProbe();
        var missionControlClient =
            new CountingMissionControlClient([true]);
        DateTimeOffset beforeCollection = DateTimeOffset.UtcNow;
        var worker = new AgentWorker(
            NullLogger<AgentWorker>.Instance,
            dockerCollector,
            hostCollector,
            missionControlClient,
            new ProtocolProbeRunner([protocolProbe]),
            fixture.SnapshotStore,
            new SnapshotPublicationGate(TimeSpan.FromHours(1)),
            Options.Create(CreateOptions()));

        await worker.ExecuteIterationAsync(
            CreateOptions(),
            CancellationToken.None);

        DateTimeOffset afterCollection = DateTimeOffset.UtcNow;
        StoredNodeSnapshot stored =
            await GetRequiredSnapshotAsync(
                fixture.SnapshotStore,
                "node-1");
        NodeSnapshotEvent snapshot = stored.Snapshot;
        Assert.Equal(1, hostCollector.CollectionCount);
        Assert.Equal(1, dockerCollector.CollectionCount);
        Assert.Equal(1, protocolProbe.ExecutionCount);
        Assert.Equal("node-1", snapshot.Node);
        Assert.InRange(
            snapshot.CapturedAt,
            beforeCollection,
            afterCollection);
        Assert.Equal(TimeSpan.Zero, snapshot.CapturedAt.Offset);
        Assert.Equal(expectedHost, snapshot.Host);
        Assert.Equal(37.5, snapshot.Host?.CpuPercent);
        Assert.Equal(17_179_869_184, snapshot.Host?.MemoryTotalBytes);
        Assert.Equal(6_442_450_944, snapshot.Host?.MemoryAvailableBytes);
        Assert.Equal(expectedContainer, Assert.Single(snapshot.Containers));
        ProtocolProbeResult protocol = Assert.Single(snapshot.Protocols);
        Assert.Equal("health", protocol.Service);
        Assert.Equal("localhost:7", protocol.Endpoint);
        Assert.True(protocol.Succeeded);
        Assert.Null(protocol.Error);
        Assert.Equal(1, missionControlClient.PublishCallCount);
        Assert.True(stored.PublishSucceeded);
        Assert.NotNull(stored.LastPublishAttemptAt);

        var publicSnapshot =
            AgentSnapshotEndpointRouteBuilderExtensions
                .CreatePublicSnapshot(
                    stored,
                    afterCollection,
                    TimeSpan.FromMinutes(1));
        using JsonContent content =
            JsonContent.Create(publicSnapshot);
        AgentSnapshotItem? dashboardSnapshot =
            await content.ReadFromJsonAsync<AgentSnapshotItem>();

        Assert.NotNull(dashboardSnapshot);
        Assert.Equal(37.5, dashboardSnapshot.Host?.CpuPercent);
        Assert.Equal(
            17_179_869_184,
            dashboardSnapshot.Host?.MemoryTotalBytes);
        Assert.Equal(
            6_442_450_944,
            dashboardSnapshot.Host?.MemoryAvailableBytes);
        Assert.True(dashboardSnapshot.DockerAvailable);
        Assert.True(
            dashboardSnapshot.MissionControlPublishSucceeded);
        Assert.Equal(
            stored.LastPublishAttemptAt,
            dashboardSnapshot.LastMissionControlPublishAttemptAt);
        Assert.Equal(
            expectedContainer.Name,
            Assert.Single(dashboardSnapshot.Containers).Name);
        Assert.Equal(
            "health",
            Assert.Single(dashboardSnapshot.Protocols).Service);
        Assert.Equal(
            10_737_418_240,
            NodeResourceCalculations.GetMemoryUsedBytes(
                dashboardSnapshot.Host));
        Assert.Equal(
            62.5,
            NodeResourceCalculations.GetMemoryPercent(
                dashboardSnapshot.Host));
        Assert.Equal(
            "10 GB",
            NodeResourceCalculations.FormatBytes(
                NodeResourceCalculations.GetMemoryUsedBytes(
                    dashboardSnapshot.Host)));
    }

    [Fact]
    public async Task ShutdownCancellationPropagatesWithoutSavingSnapshot()
    {
        using var cancellationSource =
            new CancellationTokenSource();
        cancellationSource.Cancel();

        var store = new RecordingSnapshotStore();
        AgentWorker worker = CreateWorker(
            new CancelledDockerMetricsCollector(
                cancellationSource.Token),
            store);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => worker.ExecuteIterationAsync(
                CreateOptions(),
                cancellationSource.Token));

        Assert.Null(store.SavedSnapshot);
    }

    [Fact]
    public async Task SuppressedIntervalSavesFreshMetricsWithoutRecordingAttempt()
    {
        var store = new RecordingSnapshotStore();
        AgentWorker worker = CreateWorker(
            new SuccessfulDockerMetricsCollector(),
            store,
            publishOutcomes: [true]);

        await worker.ExecuteIterationAsync(
            CreateOptions(),
            CancellationToken.None);
        await worker.ExecuteIterationAsync(
            CreateOptions(),
            CancellationToken.None);

        Assert.Equal(2, store.SavedSnapshots.Count);
        Assert.True(
            store.SavedSnapshots[1].CapturedAt >=
            store.SavedSnapshots[0].CapturedAt);
        Assert.NotEqual(
            store.SavedSnapshots[0].Host?.CpuPercent,
            store.SavedSnapshots[1].Host?.CpuPercent);
        Assert.NotEqual(
            store.SavedSnapshots[0].Host?.MemoryAvailableBytes,
            store.SavedSnapshots[1].Host?.MemoryAvailableBytes);
        Assert.Single(store.PublishResults);
        Assert.True(store.PublishResults[0].Succeeded);
        Assert.Equal(1, store.PublishObservedCount);
    }

    [Fact]
    public async Task SuppressedIntervalPersistsFreshHostMetricsAndPriorAttemptMetadata()
    {
        await using var fixture =
            await AgentSnapshotStoreFixture.CreateAsync();
        HostMetric[] hostSamples =
        [
            new HostMetric(8, 25.5, 17_179_869_184, 8_589_934_592),
            new HostMetric(8, 37.5, 17_179_869_184, 6_442_450_944)
        ];
        var hostCollector =
            new SequenceHostMetricsCollector(hostSamples);
        var dockerCollector =
            new TrackingDockerMetricsCollector([]);
        var missionControlClient =
            new CountingMissionControlClient([true]);
        var worker = new AgentWorker(
            NullLogger<AgentWorker>.Instance,
            dockerCollector,
            hostCollector,
            missionControlClient,
            new ProtocolProbeRunner([new SuccessfulProtocolProbe()]),
            fixture.SnapshotStore,
            new SnapshotPublicationGate(TimeSpan.FromHours(1)),
            Options.Create(CreateOptions()));

        await worker.ExecuteIterationAsync(
            CreateOptions(),
            CancellationToken.None);
        StoredNodeSnapshot firstStored =
            await GetRequiredSnapshotAsync(
                fixture.SnapshotStore,
                "node-1");

        await worker.ExecuteIterationAsync(
            CreateOptions(),
            CancellationToken.None);
        StoredNodeSnapshot secondStored =
            await GetRequiredSnapshotAsync(
                fixture.SnapshotStore,
                "node-1");

        Assert.Equal(2, hostCollector.CollectionCount);
        Assert.Equal(2, dockerCollector.CollectionCount);
        Assert.Equal(1, missionControlClient.PublishCallCount);
        Assert.Equal(hostSamples[1], secondStored.Snapshot.Host);
        Assert.True(
            secondStored.Snapshot.CapturedAt >=
            firstStored.Snapshot.CapturedAt);
        Assert.True(secondStored.PublishSucceeded);
        Assert.Equal(
            firstStored.LastPublishAttemptAt,
            secondStored.LastPublishAttemptAt);
        Assert.NotNull(secondStored.LastPublishAttemptAt);
    }

    [Fact]
    public async Task FailedPublicationRetriesAndSuccessSuppressesNextInterval()
    {
        var store = new RecordingSnapshotStore();
        AgentWorker worker = CreateWorker(
            new SuccessfulDockerMetricsCollector(),
            store,
            publishOutcomes: [false, true]);

        await worker.ExecuteIterationAsync(
            CreateOptions(),
            CancellationToken.None);
        await worker.ExecuteIterationAsync(
            CreateOptions(),
            CancellationToken.None);
        await worker.ExecuteIterationAsync(
            CreateOptions(),
            CancellationToken.None);

        Assert.Equal(3, store.SavedSnapshots.Count);
        Assert.Equal(2, store.PublishObservedCount);
        Assert.Collection(
            store.PublishResults,
            result => Assert.False(result.Succeeded),
            result => Assert.True(result.Succeeded));
    }

    [Fact]
    public async Task PublicationMetadataWriteFailureLeavesPublicationDue()
    {
        var store = new RecordingSnapshotStore
        {
            FailNextPublishResult = true
        };
        AgentWorker worker = CreateWorker(
            new SuccessfulDockerMetricsCollector(),
            store,
            publishOutcomes: [true, true]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => worker.ExecuteIterationAsync(
                CreateOptions(),
                CancellationToken.None));

        await worker.ExecuteIterationAsync(
            CreateOptions(),
            CancellationToken.None);

        Assert.Equal(2, store.PublishObservedCount);
        Assert.Single(store.PublishResults);
        Assert.True(store.PublishResults[0].Succeeded);
    }

    private static AgentWorker CreateWorker(
        IDockerMetricsCollector dockerMetricsCollector,
        RecordingSnapshotStore store,
        IReadOnlyList<bool>? publishOutcomes = null)
    {
        return new AgentWorker(
            NullLogger<AgentWorker>.Instance,
            dockerMetricsCollector,
            new StubHostMetricsCollector(),
            new StubMissionControlClient(
                store,
                publishOutcomes ?? [true]),
            new ProtocolProbeRunner([new SuccessfulProtocolProbe()]),
            store,
            new SnapshotPublicationGate(TimeSpan.FromHours(1)),
            Options.Create(CreateOptions()));
    }

    private static AgentOptions CreateOptions()
    {
        return new AgentOptions
        {
            NodeName = "node-1",
            DockerEnabled = true,
            Probes =
            [
                new ProbeOptions
                {
                    Name = "health",
                    Host = "localhost",
                    Protocol = "test",
                    Port = 7
                }
            ]
        };
    }

    private sealed class ThrowingDockerMetricsCollector :
        IDockerMetricsCollector
    {
        public Task<DockerMetricsCollectionResult> GetMetricsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<DockerMetricsCollectionResult>(
                new InvalidOperationException(
                    "Sensitive socket path: /var/run/docker.sock"));
        }
    }

    private sealed class SuccessfulDockerMetricsCollector :
        IDockerMetricsCollector
    {
        public Task<DockerMetricsCollectionResult> GetMetricsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new DockerMetricsCollectionResult(
                    Succeeded: true,
                    Containers: [],
                    Error: null));
        }
    }

    private sealed class CancelledDockerMetricsCollector(
        CancellationToken cancellationToken) :
        IDockerMetricsCollector
    {
        public Task<DockerMetricsCollectionResult> GetMetricsAsync(
            CancellationToken ignored = default)
        {
            return Task.FromCanceled<DockerMetricsCollectionResult>(
                cancellationToken);
        }
    }

    private sealed class StubHostMetricsCollector :
        IHostMetricsCollector
    {
        private int collectionCount;

        public Task<HostMetric> GetMetricsAsync(
            CancellationToken cancellationToken = default)
        {
            int sample = collectionCount++;

            return Task.FromResult(
                new HostMetric(
                    LogicalProcessorCount: 4,
                    CpuPercent: 25 + sample,
                    MemoryTotalBytes: 1_000,
                    MemoryAvailableBytes: 500 - sample));
        }
    }

    private sealed class TrackingHostMetricsCollector(
        HostMetric metric) :
        IHostMetricsCollector
    {
        public int CollectionCount { get; private set; }

        public Task<HostMetric> GetMetricsAsync(
            CancellationToken cancellationToken = default)
        {
            CollectionCount++;
            return Task.FromResult(metric);
        }
    }

    private sealed class SequenceHostMetricsCollector(
        IReadOnlyList<HostMetric> metrics) :
        IHostMetricsCollector
    {
        public int CollectionCount { get; private set; }

        public Task<HostMetric> GetMetricsAsync(
            CancellationToken cancellationToken = default)
        {
            HostMetric metric = metrics[CollectionCount];
            CollectionCount++;
            return Task.FromResult(metric);
        }
    }

    private sealed class TrackingDockerMetricsCollector(
        IReadOnlyList<ContainerMetric> containers) :
        IDockerMetricsCollector
    {
        public int CollectionCount { get; private set; }

        public Task<DockerMetricsCollectionResult> GetMetricsAsync(
            CancellationToken cancellationToken = default)
        {
            CollectionCount++;
            return Task.FromResult(
                new DockerMetricsCollectionResult(
                    Succeeded: true,
                    Containers: containers,
                    Error: null));
        }
    }

    private sealed class SuccessfulProtocolProbe : IProtocolProbe
    {
        public string Protocol => "test";

        public Task ExecuteAsync(
            ProbeOptions options,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingProtocolProbe : IProtocolProbe
    {
        public string Protocol => "test";

        public int ExecutionCount { get; private set; }

        public Task ExecuteAsync(
            ProbeOptions options,
            CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSnapshotStore : INodeSnapshotStore
    {
        public List<NodeSnapshotEvent> SavedSnapshots { get; } = [];

        public List<PublishResult> PublishResults { get; } = [];

        public NodeSnapshotEvent? SavedSnapshot =>
            SavedSnapshots.LastOrDefault();

        public bool FailNextPublishResult { get; set; }

        public bool PublishWasAfterSave { get; private set; }

        public int PublishObservedCount { get; private set; }

        public void RecordPublishObservation()
        {
            PublishWasAfterSave = SavedSnapshot is not null;
            PublishObservedCount++;
        }

        public Task SaveAsync(
            NodeSnapshotEvent snapshot,
            CancellationToken cancellationToken = default)
        {
            SavedSnapshots.Add(snapshot);
            return Task.CompletedTask;
        }

        public Task RecordPublishResultAsync(
            string node,
            bool succeeded,
            DateTimeOffset attemptedAt,
            CancellationToken cancellationToken = default)
        {
            if (FailNextPublishResult)
            {
                FailNextPublishResult = false;
                throw new InvalidOperationException(
                    "Publication metadata write failed.");
            }

            PublishResults.Add(
                new PublishResult(
                    succeeded,
                    attemptedAt));

            return Task.CompletedTask;
        }

        public Task<StoredNodeSnapshot?> GetAsync(
            string node,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<StoredNodeSnapshot?>(null);
        }

        public sealed record PublishResult(
            bool Succeeded,
            DateTimeOffset AttemptedAt);
    }

    private sealed class StubMissionControlClient(
        RecordingSnapshotStore store,
        IReadOnlyList<bool> outcomes) :
        IMissionControlClient
    {
        private readonly Queue<bool> publishOutcomes =
            new(outcomes);

        public Task<bool> TryPublishAsync<TPayload>(
            string eventType,
            TPayload payload,
            DateTimeOffset occurredAt,
            string? correlationId,
            CancellationToken cancellationToken)
        {
            store.RecordPublishObservation();
            return Task.FromResult(
                publishOutcomes.Count > 0 &&
                publishOutcomes.Dequeue());
        }
    }

    private sealed class CountingMissionControlClient(
        IReadOnlyList<bool> outcomes) :
        IMissionControlClient
    {
        private readonly Queue<bool> publishOutcomes =
            new(outcomes);

        public int PublishCallCount { get; private set; }

        public Task<bool> TryPublishAsync<TPayload>(
            string eventType,
            TPayload payload,
            DateTimeOffset occurredAt,
            string? correlationId,
            CancellationToken cancellationToken)
        {
            PublishCallCount++;
            return Task.FromResult(publishOutcomes.Dequeue());
        }
    }

    private static async Task<StoredNodeSnapshot> GetRequiredSnapshotAsync(
        SqliteNodeSnapshotStore store,
        string node)
    {
        return await store.GetAsync(node, CancellationToken.None) ??
            throw new Xunit.Sdk.XunitException(
                $"Expected snapshot for node '{node}'.");
    }
}
