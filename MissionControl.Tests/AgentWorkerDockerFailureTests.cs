extern alias AgentApp;

using AgentApp::MissionControl.Agent;
using AgentApp::MissionControl.Agent.Docker;
using AgentApp::MissionControl.Agent.Host;
using AgentApp::MissionControl.Agent.Models;
using AgentApp::MissionControl.Agent.Protocols;
using AgentApp::MissionControl.Agent.Publishing;
using AgentApp::MissionControl.Agent.Storage;
using JoyfulReaperLib.MissionControl;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
        Assert.Single(store.PublishResults);
        Assert.True(store.PublishResults[0].Succeeded);
        Assert.Equal(1, store.PublishObservedCount);
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
            return Task.FromResult(
                new HostMetric(
                    LogicalProcessorCount: 4,
                    CpuPercent: 25 + collectionCount++,
                    MemoryTotalBytes: 1_000,
                    MemoryAvailableBytes: 500));
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
}
