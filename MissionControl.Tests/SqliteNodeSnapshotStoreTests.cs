extern alias AgentApp;

using Dapper;
using Microsoft.Data.Sqlite;
using AgentApp::MissionControl.Agent.Models;
using AgentApp::MissionControl.Agent.Storage;
using System.Text.Json;
using Xunit;

namespace MissionControl.Tests;

public sealed class SqliteNodeSnapshotStoreTests
{
    [Fact]
    public async Task GetAsyncForUnknownNodeReturnsNull()
    {
        await using var fixture =
            await AgentSnapshotStoreFixture.CreateAsync();

        StoredNodeSnapshot? snapshot =
            await fixture.SnapshotStore.GetAsync(
                "missing-node",
                CancellationToken.None);

        Assert.Null(snapshot);
    }

    [Fact]
    public async Task SaveAsyncRoundTripsSnapshotAndMetadata()
    {
        await using var fixture =
            await AgentSnapshotStoreFixture.CreateAsync();

        NodeSnapshotEvent snapshot = CreateSnapshot();
        DateTimeOffset beforeSave = DateTimeOffset.UtcNow;

        await fixture.SnapshotStore.SaveAsync(
            snapshot,
            CancellationToken.None);

        DateTimeOffset afterSave = DateTimeOffset.UtcNow;
        StoredNodeSnapshot stored =
            await GetRequiredSnapshotAsync(
                fixture.SnapshotStore,
                snapshot.Node);

        Assert.Equal(snapshot.Node, stored.Snapshot.Node);
        Assert.Equal(snapshot.CapturedAt, stored.Snapshot.CapturedAt);
        Assert.Equal(snapshot.Host, stored.Snapshot.Host);
        AssertProtocolResultsEqual(
            snapshot.Protocols,
            stored.Snapshot.Protocols);
        AssertContainerMetricsEqual(
            snapshot.Containers,
            stored.Snapshot.Containers);
        Assert.Equal(
            snapshot.DockerAvailable,
            stored.Snapshot.DockerAvailable);
        Assert.Equal(
            snapshot.DockerError,
            stored.Snapshot.DockerError);
        Assert.Null(stored.PublishSucceeded);
        Assert.Null(stored.LastPublishAttemptAt);
        Assert.Equal(TimeSpan.Zero, stored.UpdatedAt.Offset);
        Assert.InRange(stored.UpdatedAt, beforeSave, afterSave);
    }

    [Fact]
    public async Task MixedContainerStatesAndUnavailableMetricsRoundTrip()
    {
        await using var fixture =
            await AgentSnapshotStoreFixture.CreateAsync();
        DateTimeOffset capturedAt = DateTimeOffset.UtcNow;
        var snapshot = new NodeSnapshotEvent(
            Node: "mixed-container-node",
            CapturedAt: capturedAt,
            Host: null,
            Protocols: [],
            Containers:
            [
                new ContainerMetric(
                    "api",
                    "missioncontrol/api:1",
                    "running",
                    100,
                    1_000,
                    10,
                    5,
                    1),
                new ContainerMetric(
                    "worker",
                    "missioncontrol/worker:1",
                    "exited",
                    null,
                    null,
                    null,
                    null,
                    4),
                new ContainerMetric(
                    "scheduler",
                    "missioncontrol/scheduler:1",
                    "stopped",
                    null,
                    null,
                    null,
                    null,
                    null)
            ],
            DockerAvailable: true,
            DockerError: null);

        await fixture.SnapshotStore.SaveAsync(
            snapshot,
            CancellationToken.None);

        StoredNodeSnapshot stored =
            await GetRequiredSnapshotAsync(
                fixture.SnapshotStore,
                snapshot.Node);

        AssertContainerMetricsEqual(
            snapshot.Containers,
            stored.Snapshot.Containers);
        Assert.True(stored.Snapshot.DockerAvailable);
        Assert.Null(stored.Snapshot.DockerError);
        ContainerMetric exited = stored.Snapshot.Containers[1];
        Assert.Equal("exited", exited.State);
        Assert.Null(exited.MemoryUsageBytes);
        Assert.Null(exited.CpuPercent);
        Assert.Equal(4, exited.RestartCount);
    }

    [Fact]
    public async Task RecordPublishResultAsyncStoresSuccessfulPublication()
    {
        await using var fixture =
            await AgentSnapshotStoreFixture.CreateAsync();

        NodeSnapshotEvent snapshot = CreateSnapshot();
        DateTimeOffset attemptedAt =
            new(2026, 7, 15, 9, 15, 0, TimeSpan.FromHours(-4));

        await fixture.SnapshotStore.SaveAsync(
            snapshot,
            CancellationToken.None);
        await fixture.SnapshotStore.RecordPublishResultAsync(
            snapshot.Node,
            succeeded: true,
            attemptedAt,
            CancellationToken.None);

        StoredNodeSnapshot stored =
            await GetRequiredSnapshotAsync(
                fixture.SnapshotStore,
                snapshot.Node);

        Assert.True(stored.PublishSucceeded);
        Assert.Equal(
            attemptedAt.ToUniversalTime(),
            stored.LastPublishAttemptAt);
        Assert.Equal(
            TimeSpan.Zero,
            stored.LastPublishAttemptAt?.Offset);
        Assert.Equal(snapshot.Node, stored.Snapshot.Node);
        Assert.Equal(snapshot.CapturedAt, stored.Snapshot.CapturedAt);
        AssertProtocolResultsEqual(
            snapshot.Protocols,
            stored.Snapshot.Protocols);
        AssertContainerMetricsEqual(
            snapshot.Containers,
            stored.Snapshot.Containers);
    }

    [Fact]
    public async Task RecordPublishResultAsyncStoresFailedPublication()
    {
        await using var fixture =
            await AgentSnapshotStoreFixture.CreateAsync();

        NodeSnapshotEvent snapshot = CreateSnapshot();
        DateTimeOffset successfulAttemptAt =
            new(2026, 7, 15, 13, 15, 0, TimeSpan.Zero);
        DateTimeOffset failedAttemptAt =
            new(2026, 7, 15, 13, 20, 0, TimeSpan.Zero);

        await fixture.SnapshotStore.SaveAsync(
            snapshot,
            CancellationToken.None);
        await fixture.SnapshotStore.RecordPublishResultAsync(
            snapshot.Node,
            succeeded: true,
            successfulAttemptAt,
            CancellationToken.None);
        await fixture.SnapshotStore.RecordPublishResultAsync(
            snapshot.Node,
            succeeded: false,
            failedAttemptAt,
            CancellationToken.None);

        StoredNodeSnapshot stored =
            await GetRequiredSnapshotAsync(
                fixture.SnapshotStore,
                snapshot.Node);

        Assert.False(stored.PublishSucceeded);
        Assert.Equal(failedAttemptAt, stored.LastPublishAttemptAt);
    }

    [Fact]
    public async Task SavingNewerSnapshotPreservesSuccessfulMetadataAcrossStoreRestart()
    {
        await using var fixture =
            await AgentSnapshotStoreFixture.CreateAsync();

        NodeSnapshotEvent originalSnapshot = CreateSnapshot();
        NodeSnapshotEvent replacementSnapshot =
            CreateSnapshot(
                capturedAt: originalSnapshot.CapturedAt.AddMinutes(5),
                protocolSuffix: "replacement",
                containerSuffix: "replacement",
                cpuPercent: 47.75,
                memoryTotalBytes: 17_179_869_184,
                memoryAvailableBytes: 6_442_450_944);
        DateTimeOffset attemptedAt =
            originalSnapshot.CapturedAt.AddMinutes(1);

        await fixture.SnapshotStore.SaveAsync(
            originalSnapshot,
            CancellationToken.None);
        await fixture.SnapshotStore.RecordPublishResultAsync(
            originalSnapshot.Node,
            succeeded: true,
            attemptedAt,
            CancellationToken.None);
        await fixture.SnapshotStore.SaveAsync(
            replacementSnapshot,
            CancellationToken.None);

        var restartedStore =
            new SqliteNodeSnapshotStore(fixture.Database);
        StoredNodeSnapshot stored =
            await GetRequiredSnapshotAsync(
                restartedStore,
                replacementSnapshot.Node);

        Assert.Equal(
            replacementSnapshot.CapturedAt,
            stored.Snapshot.CapturedAt);
        Assert.Equal(
            replacementSnapshot.Host,
            stored.Snapshot.Host);
        AssertProtocolResultsEqual(
            replacementSnapshot.Protocols,
            stored.Snapshot.Protocols);
        AssertContainerMetricsEqual(
            replacementSnapshot.Containers,
            stored.Snapshot.Containers);
        Assert.True(stored.PublishSucceeded);
        Assert.Equal(attemptedAt, stored.LastPublishAttemptAt);
    }

    [Fact]
    public async Task SuppressedSnapshotsPreserveFailureUntilLaterSuccess()
    {
        await using var fixture =
            await AgentSnapshotStoreFixture.CreateAsync();

        NodeSnapshotEvent firstSnapshot = CreateSnapshot();
        NodeSnapshotEvent suppressedSnapshot =
            CreateSnapshot(
                capturedAt: firstSnapshot.CapturedAt.AddMinutes(1),
                protocolSuffix: "suppressed",
                containerSuffix: "suppressed");
        NodeSnapshotEvent latestSnapshot =
            CreateSnapshot(
                capturedAt: firstSnapshot.CapturedAt.AddMinutes(2),
                protocolSuffix: "latest",
                containerSuffix: "latest");
        DateTimeOffset failedAttemptAt =
            firstSnapshot.CapturedAt.AddSeconds(10);
        DateTimeOffset successfulAttemptAt =
            firstSnapshot.CapturedAt.AddMinutes(2).AddSeconds(10);

        await fixture.SnapshotStore.SaveAsync(
            firstSnapshot,
            CancellationToken.None);
        await fixture.SnapshotStore.RecordPublishResultAsync(
            firstSnapshot.Node,
            succeeded: false,
            failedAttemptAt,
            CancellationToken.None);
        await fixture.SnapshotStore.SaveAsync(
            suppressedSnapshot,
            CancellationToken.None);

        StoredNodeSnapshot afterSuppressedSave =
            await GetRequiredSnapshotAsync(
                fixture.SnapshotStore,
                firstSnapshot.Node);

        Assert.Equal(
            suppressedSnapshot.CapturedAt,
            afterSuppressedSave.Snapshot.CapturedAt);
        AssertProtocolResultsEqual(
            suppressedSnapshot.Protocols,
            afterSuppressedSave.Snapshot.Protocols);
        Assert.False(afterSuppressedSave.PublishSucceeded);
        Assert.Equal(
            failedAttemptAt,
            afterSuppressedSave.LastPublishAttemptAt);

        await fixture.SnapshotStore.SaveAsync(
            latestSnapshot,
            CancellationToken.None);
        await fixture.SnapshotStore.RecordPublishResultAsync(
            latestSnapshot.Node,
            succeeded: true,
            successfulAttemptAt,
            CancellationToken.None);

        StoredNodeSnapshot afterSuccessfulAttempt =
            await GetRequiredSnapshotAsync(
                fixture.SnapshotStore,
                firstSnapshot.Node);

        Assert.Equal(
            latestSnapshot.CapturedAt,
            afterSuccessfulAttempt.Snapshot.CapturedAt);
        AssertContainerMetricsEqual(
            latestSnapshot.Containers,
            afterSuccessfulAttempt.Snapshot.Containers);
        Assert.True(afterSuccessfulAttempt.PublishSucceeded);
        Assert.Equal(
            successfulAttemptAt,
            afterSuccessfulAttempt.LastPublishAttemptAt);
    }

    [Fact]
    public async Task PublishResultUpdatesMetadataWithoutOverwritingNewerSnapshot()
    {
        await using var fixture =
            await AgentSnapshotStoreFixture.CreateAsync();

        NodeSnapshotEvent snapshotA = CreateSnapshot();
        NodeSnapshotEvent snapshotB =
            CreateSnapshot(
                capturedAt: snapshotA.CapturedAt.AddMinutes(10),
                protocolSuffix: "snapshot-b",
                containerSuffix: "snapshot-b");
        DateTimeOffset attemptedAt =
            snapshotA.CapturedAt.AddMinutes(1);

        await fixture.SnapshotStore.SaveAsync(
            snapshotA,
            CancellationToken.None);
        await fixture.SnapshotStore.SaveAsync(
            snapshotB,
            CancellationToken.None);
        await fixture.SnapshotStore.RecordPublishResultAsync(
            snapshotA.Node,
            succeeded: true,
            attemptedAt,
            CancellationToken.None);

        StoredNodeSnapshot stored =
            await GetRequiredSnapshotAsync(
                fixture.SnapshotStore,
                snapshotB.Node);

        Assert.Equal(snapshotB.CapturedAt, stored.Snapshot.CapturedAt);
        AssertProtocolResultsEqual(
            snapshotB.Protocols,
            stored.Snapshot.Protocols);
        AssertContainerMetricsEqual(
            snapshotB.Containers,
            stored.Snapshot.Containers);
        Assert.True(stored.PublishSucceeded);
        Assert.Equal(attemptedAt, stored.LastPublishAttemptAt);
    }

    [Fact]
    public async Task SavingOlderSnapshotDoesNotOverwriteCurrentSnapshot()
    {
        await using var fixture =
            await AgentSnapshotStoreFixture.CreateAsync();

        NodeSnapshotEvent olderSnapshot =
            CreateSnapshot(
                capturedAt: new DateTimeOffset(
                    2026,
                    7,
                    15,
                    12,
                    34,
                    56,
                    TimeSpan.Zero),
                protocolSuffix: "older",
                containerSuffix: "older");
        NodeSnapshotEvent newerSnapshot =
            CreateSnapshot(
                capturedAt: olderSnapshot.CapturedAt.AddMinutes(10),
                protocolSuffix: "newer",
                containerSuffix: "newer");
        DateTimeOffset attemptedAt =
            newerSnapshot.CapturedAt.AddMinutes(1);

        await fixture.SnapshotStore.SaveAsync(
            newerSnapshot,
            CancellationToken.None);
        await fixture.SnapshotStore.RecordPublishResultAsync(
            newerSnapshot.Node,
            succeeded: true,
            attemptedAt,
            CancellationToken.None);

        StoredNodeSnapshot beforeStaleSave =
            await GetRequiredSnapshotAsync(
                fixture.SnapshotStore,
                newerSnapshot.Node);

        await fixture.SnapshotStore.SaveAsync(
            olderSnapshot,
            CancellationToken.None);

        StoredNodeSnapshot afterStaleSave =
            await GetRequiredSnapshotAsync(
                fixture.SnapshotStore,
                newerSnapshot.Node);

        Assert.Equal(
            newerSnapshot.CapturedAt,
            afterStaleSave.Snapshot.CapturedAt);
        AssertProtocolResultsEqual(
            newerSnapshot.Protocols,
            afterStaleSave.Snapshot.Protocols);
        AssertContainerMetricsEqual(
            newerSnapshot.Containers,
            afterStaleSave.Snapshot.Containers);
        Assert.True(afterStaleSave.PublishSucceeded);
        Assert.Equal(attemptedAt, afterStaleSave.LastPublishAttemptAt);
        Assert.Equal(beforeStaleSave.UpdatedAt, afterStaleSave.UpdatedAt);
    }

    [Fact]
    public async Task SavingSnapshotWithSameCapturedAtPreservesPublicationMetadata()
    {
        await using var fixture =
            await AgentSnapshotStoreFixture.CreateAsync();

        NodeSnapshotEvent snapshotA = CreateSnapshot();
        NodeSnapshotEvent snapshotB =
            CreateSnapshot(
                capturedAt: snapshotA.CapturedAt,
                protocolSuffix: "same-time",
                containerSuffix: "same-time");
        DateTimeOffset attemptedAt =
            snapshotA.CapturedAt.AddMinutes(1);

        await fixture.SnapshotStore.SaveAsync(
            snapshotA,
            CancellationToken.None);
        await fixture.SnapshotStore.RecordPublishResultAsync(
            snapshotA.Node,
            succeeded: true,
            attemptedAt,
            CancellationToken.None);
        await fixture.SnapshotStore.SaveAsync(
            snapshotB,
            CancellationToken.None);

        StoredNodeSnapshot stored =
            await GetRequiredSnapshotAsync(
                fixture.SnapshotStore,
                snapshotB.Node);

        Assert.Equal(snapshotB.CapturedAt, stored.Snapshot.CapturedAt);
        AssertProtocolResultsEqual(
            snapshotB.Protocols,
            stored.Snapshot.Protocols);
        AssertContainerMetricsEqual(
            snapshotB.Containers,
            stored.Snapshot.Containers);
        Assert.True(stored.PublishSucceeded);
        Assert.Equal(attemptedAt, stored.LastPublishAttemptAt);
    }

    [Fact]
    public async Task NodesRemainIsolated()
    {
        await using var fixture =
            await AgentSnapshotStoreFixture.CreateAsync();

        NodeSnapshotEvent alphaSnapshot =
            CreateSnapshot(
                node: "alpha-node",
                protocolSuffix: "alpha",
                containerSuffix: "alpha");
        NodeSnapshotEvent bravoSnapshot =
            CreateSnapshot(
                node: "bravo-node",
                capturedAt: alphaSnapshot.CapturedAt.AddMinutes(1),
                protocolSuffix: "bravo",
                containerSuffix: "bravo");

        await fixture.SnapshotStore.SaveAsync(
            alphaSnapshot,
            CancellationToken.None);
        await fixture.SnapshotStore.SaveAsync(
            bravoSnapshot,
            CancellationToken.None);
        DateTimeOffset alphaAttemptAt =
            alphaSnapshot.CapturedAt.AddSeconds(10);
        DateTimeOffset bravoAttemptAt =
            bravoSnapshot.CapturedAt.AddSeconds(10);
        await fixture.SnapshotStore.RecordPublishResultAsync(
            alphaSnapshot.Node,
            succeeded: true,
            alphaAttemptAt,
            CancellationToken.None);
        await fixture.SnapshotStore.RecordPublishResultAsync(
            bravoSnapshot.Node,
            succeeded: false,
            bravoAttemptAt,
            CancellationToken.None);

        StoredNodeSnapshot alphaStored =
            await GetRequiredSnapshotAsync(
                fixture.SnapshotStore,
                alphaSnapshot.Node);
        StoredNodeSnapshot bravoStored =
            await GetRequiredSnapshotAsync(
                fixture.SnapshotStore,
                bravoSnapshot.Node);

        Assert.Equal(alphaSnapshot.CapturedAt, alphaStored.Snapshot.CapturedAt);
        AssertProtocolResultsEqual(
            alphaSnapshot.Protocols,
            alphaStored.Snapshot.Protocols);
        AssertContainerMetricsEqual(
            alphaSnapshot.Containers,
            alphaStored.Snapshot.Containers);
        Assert.True(alphaStored.PublishSucceeded);
        Assert.Equal(
            alphaAttemptAt,
            alphaStored.LastPublishAttemptAt);
        Assert.Equal(bravoSnapshot.CapturedAt, bravoStored.Snapshot.CapturedAt);
        AssertProtocolResultsEqual(
            bravoSnapshot.Protocols,
            bravoStored.Snapshot.Protocols);
        AssertContainerMetricsEqual(
            bravoSnapshot.Containers,
            bravoStored.Snapshot.Containers);
        Assert.False(bravoStored.PublishSucceeded);
        Assert.Equal(
            bravoAttemptAt,
            bravoStored.LastPublishAttemptAt);
    }

    [Fact]
    public async Task SaveAsyncRejectsNullSnapshot()
    {
        await using var fixture =
            await AgentSnapshotStoreFixture.CreateAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => fixture.SnapshotStore.SaveAsync(
                null!,
                CancellationToken.None));
    }

    [Fact]
    public async Task GetAsyncRejectsNullNodeName()
    {
        await using var fixture =
            await AgentSnapshotStoreFixture.CreateAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => fixture.SnapshotStore.GetAsync(
                null!,
                CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task GetAsyncRejectsEmptyOrWhitespaceNodeNames(
        string node)
    {
        await using var fixture =
            await AgentSnapshotStoreFixture.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => fixture.SnapshotStore.GetAsync(
                node,
                CancellationToken.None));
    }

    [Fact]
    public async Task RecordPublishResultAsyncRejectsNullNodeName()
    {
        await using var fixture =
            await AgentSnapshotStoreFixture.CreateAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => fixture.SnapshotStore.RecordPublishResultAsync(
                null!,
                succeeded: true,
                attemptedAt: DateTimeOffset.UtcNow,
                CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task RecordPublishResultAsyncRejectsInvalidNodeNames(
        string node)
    {
        await using var fixture =
            await AgentSnapshotStoreFixture.CreateAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => fixture.SnapshotStore.RecordPublishResultAsync(
                node,
                succeeded: true,
                attemptedAt: DateTimeOffset.UtcNow,
                CancellationToken.None));
    }

    [Fact]
    public async Task RecordPublishResultAsyncRejectsUnknownNode()
    {
        await using var fixture =
            await AgentSnapshotStoreFixture.CreateAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.SnapshotStore.RecordPublishResultAsync(
                "missing-node",
                succeeded: true,
                attemptedAt: DateTimeOffset.UtcNow,
                CancellationToken.None));

        Assert.Null(
            await fixture.SnapshotStore.GetAsync(
                "missing-node",
                CancellationToken.None));
    }

    [Fact]
    public async Task GetAsyncThrowsForCorruptPersistedPayload()
    {
        await using var fixture =
            await AgentSnapshotStoreFixture.CreateAsync();

        NodeSnapshotEvent snapshot = CreateSnapshot();

        await fixture.SnapshotStore.SaveAsync(
            snapshot,
            CancellationToken.None);

        await using (SqliteConnection connection =
            fixture.Database.CreateConnection())
        {
            await connection.OpenAsync(CancellationToken.None);
            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    UPDATE NodeSnapshots
                    SET Payload = @Payload
                    WHERE Node = @Node;
                    """,
                    new
                    {
                        Node = snapshot.Node,
                        Payload = "{not valid json"
                    },
                    cancellationToken: CancellationToken.None));
        }

        await Assert.ThrowsAnyAsync<JsonException>(
            () => fixture.SnapshotStore.GetAsync(
                snapshot.Node,
                CancellationToken.None));
    }

    [Fact]
    public async Task GetAsyncReadsSnapshotStoredBeforeHostMetricsWereAdded()
    {
        await using var fixture =
            await AgentSnapshotStoreFixture.CreateAsync();

        NodeSnapshotEvent snapshot = CreateSnapshot();

        await fixture.SnapshotStore.SaveAsync(
            snapshot,
            CancellationToken.None);

        await using (SqliteConnection connection =
            fixture.Database.CreateConnection())
        {
            await connection.OpenAsync(CancellationToken.None);
            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    UPDATE NodeSnapshots
                    SET Payload = json_remove(Payload, '$.host')
                    WHERE Node = @Node;
                    """,
                    new { snapshot.Node },
                    cancellationToken: CancellationToken.None));
        }

        StoredNodeSnapshot stored =
            await GetRequiredSnapshotAsync(
                fixture.SnapshotStore,
                snapshot.Node);

        Assert.Null(stored.Snapshot.Host);
        AssertProtocolResultsEqual(
            snapshot.Protocols,
            stored.Snapshot.Protocols);
        AssertContainerMetricsEqual(
            snapshot.Containers,
            stored.Snapshot.Containers);
    }

    private static NodeSnapshotEvent CreateSnapshot(
        string node = "node-01",
        DateTimeOffset? capturedAt = null,
        string protocolSuffix = "baseline",
        string containerSuffix = "baseline",
        double cpuPercent = 32.2,
        long memoryTotalBytes = 4_000_000_000,
        long memoryAvailableBytes = 2_500_000_000)
    {
        DateTimeOffset effectiveCapturedAt =
            capturedAt ??
            new DateTimeOffset(
                2026,
                7,
                15,
                12,
                34,
                56,
                TimeSpan.Zero);

        return new NodeSnapshotEvent(
            Node: node,
            CapturedAt: effectiveCapturedAt,
            Host: new HostMetric(
                LogicalProcessorCount: 4,
                CpuPercent: cpuPercent,
                MemoryTotalBytes: memoryTotalBytes,
                MemoryAvailableBytes: memoryAvailableBytes),
            Protocols:
            [
                new ProtocolProbeResult(
                    Service: $"echo-{protocolSuffix}",
                    Endpoint: "tcp://example.internal:7",
                    Succeeded: true,
                    DurationMilliseconds: 18,
                    Error: null),
                new ProtocolProbeResult(
                    Service: $"finger-{protocolSuffix}",
                    Endpoint: "tcp://example.internal:79",
                    Succeeded: false,
                    DurationMilliseconds: 1240,
                    Error: "Connection refused")
            ],
            Containers:
            [
                new ContainerMetric(
                    Name: $"api-{containerSuffix}",
                    Image: "missioncontrol/api:1.2.3",
                    State: "running",
                    MemoryUsageBytes: 120_000_000,
                    MemoryLimitBytes: 512_000_000,
                    MemoryPercent: 23.44,
                    CpuPercent: 11.8,
                    RestartCount: 1),
                new ContainerMetric(
                    Name: $"worker-{containerSuffix}",
                    Image: "missioncontrol/worker:4.5.6",
                    State: "restarting",
                    MemoryUsageBytes: 32_000_000,
                    MemoryLimitBytes: 256_000_000,
                    MemoryPercent: 12.5,
                    CpuPercent: 3.2,
                    RestartCount: 4)
            ],
            DockerAvailable: false,
            DockerError: "Docker metric collection is unavailable.");
    }

    private static async Task<StoredNodeSnapshot> GetRequiredSnapshotAsync(
        SqliteNodeSnapshotStore store,
        string node)
    {
        return await store.GetAsync(node, CancellationToken.None) ??
            throw new Xunit.Sdk.XunitException(
                $"Expected snapshot for node '{node}'.");
    }

    private static void AssertProtocolResultsEqual(
        IReadOnlyList<ProtocolProbeResult> expected,
        IReadOnlyList<ProtocolProbeResult> actual)
    {
        Assert.Equal(expected.Count, actual.Count);

        for (int index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].Service, actual[index].Service);
            Assert.Equal(expected[index].Endpoint, actual[index].Endpoint);
            Assert.Equal(expected[index].Succeeded, actual[index].Succeeded);
            Assert.Equal(
                expected[index].DurationMilliseconds,
                actual[index].DurationMilliseconds);
            Assert.Equal(expected[index].Error, actual[index].Error);
        }
    }

    private static void AssertContainerMetricsEqual(
        IReadOnlyList<ContainerMetric> expected,
        IReadOnlyList<ContainerMetric> actual)
    {
        Assert.Equal(expected.Count, actual.Count);

        for (int index = 0; index < expected.Count; index++)
        {
            Assert.Equal(expected[index].Name, actual[index].Name);
            Assert.Equal(expected[index].Image, actual[index].Image);
            Assert.Equal(expected[index].State, actual[index].State);
            Assert.Equal(
                expected[index].MemoryUsageBytes,
                actual[index].MemoryUsageBytes);
            Assert.Equal(
                expected[index].MemoryLimitBytes,
                actual[index].MemoryLimitBytes);
            Assert.Equal(
                expected[index].MemoryPercent,
                actual[index].MemoryPercent);
            Assert.Equal(expected[index].CpuPercent, actual[index].CpuPercent);
            Assert.Equal(
                expected[index].RestartCount,
                actual[index].RestartCount);
        }
    }
}
