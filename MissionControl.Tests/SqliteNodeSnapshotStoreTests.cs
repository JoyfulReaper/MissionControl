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
        AssertProtocolResultsEqual(
            snapshot.Protocols,
            stored.Snapshot.Protocols);
        AssertContainerMetricsEqual(
            snapshot.Containers,
            stored.Snapshot.Containers);
        Assert.Null(stored.PublishSucceeded);
        Assert.Null(stored.LastPublishAttemptAt);
        Assert.Equal(TimeSpan.Zero, stored.UpdatedAt.Offset);
        Assert.InRange(stored.UpdatedAt, beforeSave, afterSave);
    }

    [Fact]
    public async Task RecordPublishResultAsyncStoresSuccessfulPublication()
    {
        await using var fixture =
            await AgentSnapshotStoreFixture.CreateAsync();

        NodeSnapshotEvent snapshot = CreateSnapshot();
        DateTimeOffset attemptedAt =
            new(2026, 7, 15, 13, 15, 0, TimeSpan.Zero);

        await fixture.SnapshotStore.SaveAsync(
            snapshot,
            CancellationToken.None);
        await fixture.SnapshotStore.RecordPublishResultAsync(
            snapshot.Node,
            snapshot.CapturedAt,
            succeeded: true,
            attemptedAt,
            CancellationToken.None);

        StoredNodeSnapshot stored =
            await GetRequiredSnapshotAsync(
                fixture.SnapshotStore,
                snapshot.Node);

        Assert.True(stored.PublishSucceeded);
        Assert.Equal(attemptedAt, stored.LastPublishAttemptAt);
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
        DateTimeOffset attemptedAt =
            new(2026, 7, 15, 13, 20, 0, TimeSpan.Zero);

        await fixture.SnapshotStore.SaveAsync(
            snapshot,
            CancellationToken.None);
        await fixture.SnapshotStore.RecordPublishResultAsync(
            snapshot.Node,
            snapshot.CapturedAt,
            succeeded: false,
            attemptedAt,
            CancellationToken.None);

        StoredNodeSnapshot stored =
            await GetRequiredSnapshotAsync(
                fixture.SnapshotStore,
                snapshot.Node);

        Assert.False(stored.PublishSucceeded);
        Assert.Equal(attemptedAt, stored.LastPublishAttemptAt);
    }

    [Fact]
    public async Task SavingReplacementSnapshotOverwritesCurrentSnapshotAndResetsPublicationMetadata()
    {
        await using var fixture =
            await AgentSnapshotStoreFixture.CreateAsync();

        NodeSnapshotEvent originalSnapshot = CreateSnapshot();
        NodeSnapshotEvent replacementSnapshot =
            CreateSnapshot(
                capturedAt: originalSnapshot.CapturedAt.AddMinutes(-5),
                protocolSuffix: "replacement",
                containerSuffix: "replacement");

        await fixture.SnapshotStore.SaveAsync(
            originalSnapshot,
            CancellationToken.None);
        await fixture.SnapshotStore.RecordPublishResultAsync(
            originalSnapshot.Node,
            originalSnapshot.CapturedAt,
            succeeded: true,
            attemptedAt: originalSnapshot.CapturedAt.AddMinutes(1),
            CancellationToken.None);
        await fixture.SnapshotStore.SaveAsync(
            replacementSnapshot,
            CancellationToken.None);

        StoredNodeSnapshot stored =
            await GetRequiredSnapshotAsync(
                fixture.SnapshotStore,
                replacementSnapshot.Node);

        Assert.Equal(
            replacementSnapshot.CapturedAt,
            stored.Snapshot.CapturedAt);
        AssertProtocolResultsEqual(
            replacementSnapshot.Protocols,
            stored.Snapshot.Protocols);
        AssertContainerMetricsEqual(
            replacementSnapshot.Containers,
            stored.Snapshot.Containers);
        Assert.Null(stored.PublishSucceeded);
        Assert.Null(stored.LastPublishAttemptAt);
    }

    [Fact]
    public async Task StalePublishResultDoesNotUpdateReplacedSnapshot()
    {
        await using var fixture =
            await AgentSnapshotStoreFixture.CreateAsync();

        NodeSnapshotEvent snapshotA = CreateSnapshot();
        NodeSnapshotEvent snapshotB =
            CreateSnapshot(
                capturedAt: snapshotA.CapturedAt.AddMinutes(-10),
                protocolSuffix: "snapshot-b",
                containerSuffix: "snapshot-b");

        await fixture.SnapshotStore.SaveAsync(
            snapshotA,
            CancellationToken.None);
        await fixture.SnapshotStore.SaveAsync(
            snapshotB,
            CancellationToken.None);
        await fixture.SnapshotStore.RecordPublishResultAsync(
            snapshotA.Node,
            snapshotA.CapturedAt,
            succeeded: true,
            attemptedAt: snapshotA.CapturedAt.AddMinutes(1),
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
        Assert.Null(stored.PublishSucceeded);
        Assert.Null(stored.LastPublishAttemptAt);
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
        Assert.Equal(bravoSnapshot.CapturedAt, bravoStored.Snapshot.CapturedAt);
        AssertProtocolResultsEqual(
            bravoSnapshot.Protocols,
            bravoStored.Snapshot.Protocols);
        AssertContainerMetricsEqual(
            bravoSnapshot.Containers,
            bravoStored.Snapshot.Containers);
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
                CreateSnapshot().CapturedAt,
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
                CreateSnapshot().CapturedAt,
                succeeded: true,
                attemptedAt: DateTimeOffset.UtcNow,
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

    private static NodeSnapshotEvent CreateSnapshot(
        string node = "node-01",
        DateTimeOffset? capturedAt = null,
        string protocolSuffix = "baseline",
        string containerSuffix = "baseline")
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
            ]);
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
