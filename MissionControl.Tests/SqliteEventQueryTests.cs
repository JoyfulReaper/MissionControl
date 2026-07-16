/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */

extern alias ArchiveApp;
using ArchiveApp::MissionControl.Archive.Contracts;
using ArchiveApp::MissionControl.Archive.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using MissionControl.Contracts;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace MissionControl.Tests;

public sealed class SqliteEventQueryTests
{
    static SqliteEventQueryTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    [Fact]
    public async Task GetRecentSummariesAsyncDoesNotSkipEventsWithTiedTimestamps()
    {
        await WithArchiveDatabaseAsync(
            async (archive, query) =>
            {
                DateTimeOffset occurredAt =
                    new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

                IntegrationEventEnvelope first = CreateEnvelope(
                    Guid.Parse("40000000-0000-0000-0000-000000000004"),
                    "test.event",
                    "test",
                    occurredAt,
                    occurredAt.AddSeconds(4));

                IntegrationEventEnvelope second = CreateEnvelope(
                    Guid.Parse("40000000-0000-0000-0000-000000000003"),
                    "test.event",
                    "test",
                    occurredAt,
                    occurredAt.AddSeconds(3));

                IntegrationEventEnvelope third = CreateEnvelope(
                    Guid.Parse("40000000-0000-0000-0000-000000000002"),
                    "test.event",
                    "test",
                    occurredAt,
                    occurredAt.AddSeconds(2));

                IntegrationEventEnvelope fourth = CreateEnvelope(
                    Guid.Parse("40000000-0000-0000-0000-000000000001"),
                    "test.event",
                    "test",
                    occurredAt,
                    occurredAt.AddSeconds(1));

                await archive.StoreAsync(first);
                await archive.StoreAsync(second);
                await archive.StoreAsync(third);
                await archive.StoreAsync(fourth);

                IReadOnlyList<EventSummaryItem> firstPage =
                    await query.GetRecentSummariesAsync(2);

                Assert.Equal(2, firstPage.Count);

                EventSummaryItem cursor = firstPage[^1];

                IReadOnlyList<EventSummaryItem> secondPage =
                    await query.GetRecentSummariesAsync(
                        limit: 2,
                        beforeOccurredAt: cursor.OccurredAt,
                        beforeReceivedAt: cursor.ReceivedAt,
                        beforeEventId: cursor.EventId);

                Assert.Equal(2, secondPage.Count);

                Guid[] allIds = firstPage
                    .Concat(secondPage)
                    .Select(item => item.EventId)
                    .ToArray();

                Assert.Equal(4, allIds.Distinct().Count());
            });
    }

    [Fact]
    public async Task GetByIdAsyncReturnsCompleteMatchingEvent()
    {
        await WithArchiveDatabaseAsync(
            async (archive, query) =>
            {
                IntegrationEventEnvelope expected =
                    CreateEnvelope(
                        eventId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        eventType: "github.push.received",
                        source: "github",
                        schemaVersion: 7,
                        occurredAt: new DateTimeOffset(
                            2026,
                            7,
                            15,
                            14,
                            30,
                            0,
                            TimeSpan.Zero),
                        receivedAt: new DateTimeOffset(
                            2026,
                            7,
                            15,
                            14,
                            30,
                            5,
                            TimeSpan.Zero),
                        correlationId: "corr-123",
                        causationId: "cause-456",
                        payload: new
                        {
                            repository = "JoyfulReaper/MissionControl",
                            branch = "feature-dashboard",
                            delivery = 42
                        });

                bool stored = await archive.StoreAsync(expected);

                Assert.True(stored);

                var actual =
                    await query.GetByIdAsync(expected.EventId);

                Assert.NotNull(actual);
                Assert.Equal(expected.EventId, actual.EventId);
                Assert.Equal(expected.EventType, actual.EventType);
                Assert.Equal(expected.Source, actual.Source);
                Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
                Assert.Equal(expected.OccurredAt, actual.OccurredAt);
                Assert.Equal(expected.ReceivedAt, actual.ReceivedAt);
                Assert.Equal(expected.CorrelationId, actual.CorrelationId);
                Assert.Equal(expected.CausationId, actual.CausationId);
                Assert.Equal(
                    "JoyfulReaper/MissionControl",
                    actual.Payload.GetProperty("repository").GetString());
                Assert.Equal(
                    "feature-dashboard",
                    actual.Payload.GetProperty("branch").GetString());
                Assert.Equal(
                    42,
                    actual.Payload.GetProperty("delivery").GetInt32());
            });
    }

    [Fact]
    public async Task GetByIdAsyncReturnsNullForUnknownEvent()
    {
        await WithArchiveDatabaseAsync(
            async (_, query) =>
            {
                var actual =
                    await query.GetByIdAsync(Guid.NewGuid());

                Assert.Null(actual);
            });
    }

    [Fact]
    public async Task GetRecentSummariesAsyncReturnsNewestEventsFirst()
    {
        await WithArchiveDatabaseAsync(
            async (archive, query) =>
            {
                IntegrationEventEnvelope oldest =
                    CreateEnvelope(
                        eventId: Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                        eventType: "build.completed",
                        source: "ci",
                        occurredAt: new DateTimeOffset(
                            2026,
                            7,
                            15,
                            8,
                            0,
                            0,
                            TimeSpan.Zero),
                        receivedAt: new DateTimeOffset(
                            2026,
                            7,
                            15,
                            8,
                            0,
                            3,
                            TimeSpan.Zero));
                IntegrationEventEnvelope middle =
                    CreateEnvelope(
                        eventId: Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                        eventType: "build.completed",
                        source: "ci",
                        occurredAt: oldest.OccurredAt.AddMinutes(10),
                        receivedAt: oldest.ReceivedAt.AddMinutes(10));
                IntegrationEventEnvelope newest =
                    CreateEnvelope(
                        eventId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                        eventType: "build.completed",
                        source: "ci",
                        occurredAt: oldest.OccurredAt.AddMinutes(20),
                        receivedAt: oldest.ReceivedAt.AddMinutes(20));

                await archive.StoreAsync(oldest);
                await archive.StoreAsync(middle);
                await archive.StoreAsync(newest);

                IReadOnlyList<EventSummaryItem> summaries =
                    await query.GetRecentSummariesAsync(10);

                Assert.Collection(
                    summaries,
                    item => Assert.Equal(newest.EventId, item.EventId),
                    item => Assert.Equal(middle.EventId, item.EventId),
                    item => Assert.Equal(oldest.EventId, item.EventId));
            });
    }

    [Fact]
    public async Task GetRecentSummariesAsyncRespectsLimit()
    {
        await WithArchiveDatabaseAsync(
            async (archive, query) =>
            {
                DateTimeOffset baseline =
                    new(2026, 7, 15, 9, 0, 0, TimeSpan.Zero);

                for (int index = 0; index < 5; index++)
                {
                    await archive.StoreAsync(
                        CreateEnvelope(
                            eventId: Guid.Parse(
                                $"00000000-0000-0000-0000-{index + 1:000000000000}"),
                            eventType: "agent.snapshot.captured",
                            source: "agent",
                            occurredAt: baseline.AddMinutes(index),
                            receivedAt: baseline.AddMinutes(index).AddSeconds(5),
                            payload: new
                            {
                                node = $"node-{index}"
                            }));
                }

                IReadOnlyList<EventSummaryItem> summaries =
                    await query.GetRecentSummariesAsync(3);

                Assert.Equal(3, summaries.Count);
            });
    }

    [Fact]
    public async Task GetRecentSummariesAsyncFiltersBySource()
    {
        await WithArchiveDatabaseAsync(
            async (archive, query) =>
            {
                DateTimeOffset baseline =
                    new(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);

                IntegrationEventEnvelope githubA =
                    CreateEnvelope(
                        eventId: Guid.Parse("20000000-0000-0000-0000-000000000001"),
                        eventType: "github.push.received",
                        source: "github",
                        occurredAt: baseline,
                        receivedAt: baseline.AddSeconds(2));
                IntegrationEventEnvelope githubB =
                    CreateEnvelope(
                        eventId: Guid.Parse("20000000-0000-0000-0000-000000000002"),
                        eventType: "github.pull_request.received",
                        source: "github",
                        occurredAt: baseline.AddMinutes(2),
                        receivedAt: baseline.AddMinutes(2).AddSeconds(2));
                IntegrationEventEnvelope ci =
                    CreateEnvelope(
                        eventId: Guid.Parse("20000000-0000-0000-0000-000000000003"),
                        eventType: "build.completed",
                        source: "ci",
                        occurredAt: baseline.AddMinutes(1),
                        receivedAt: baseline.AddMinutes(1).AddSeconds(2));

                await archive.StoreAsync(githubA);
                await archive.StoreAsync(githubB);
                await archive.StoreAsync(ci);

                IReadOnlyList<EventSummaryItem> summaries =
                    await query.GetRecentSummariesAsync(
                        10,
                        source: "github");

                Assert.Equal(2, summaries.Count);
                Assert.All(
                    summaries,
                    item => Assert.Equal("github", item.Source));
                Assert.DoesNotContain(
                    summaries,
                    item => item.EventId == ci.EventId);
            });
    }

    [Fact]
    public async Task GetRecentSummariesAsyncFiltersByEventType()
    {
        await WithArchiveDatabaseAsync(
            async (archive, query) =>
            {
                DateTimeOffset baseline =
                    new(2026, 7, 15, 11, 0, 0, TimeSpan.Zero);

                IntegrationEventEnvelope matchingA =
                    CreateEnvelope(
                        eventId: Guid.Parse("30000000-0000-0000-0000-000000000001"),
                        eventType: "github.push.received",
                        source: "github",
                        occurredAt: baseline,
                        receivedAt: baseline.AddSeconds(1));
                IntegrationEventEnvelope matchingB =
                    CreateEnvelope(
                        eventId: Guid.Parse("30000000-0000-0000-0000-000000000002"),
                        eventType: "github.push.received",
                        source: "github",
                        occurredAt: baseline.AddMinutes(2),
                        receivedAt: baseline.AddMinutes(2).AddSeconds(1));
                IntegrationEventEnvelope nonMatching =
                    CreateEnvelope(
                        eventId: Guid.Parse("30000000-0000-0000-0000-000000000003"),
                        eventType: "github.pull_request.received",
                        source: "github",
                        occurredAt: baseline.AddMinutes(1),
                        receivedAt: baseline.AddMinutes(1).AddSeconds(1));

                await archive.StoreAsync(matchingA);
                await archive.StoreAsync(matchingB);
                await archive.StoreAsync(nonMatching);

                IReadOnlyList<EventSummaryItem> summaries =
                    await query.GetRecentSummariesAsync(
                        10,
                        eventType: "github.push.received");

                Assert.Equal(2, summaries.Count);
                Assert.All(
                    summaries,
                    item => Assert.Equal(
                        "github.push.received",
                        item.EventType));
                Assert.DoesNotContain(
                    summaries,
                    item => item.EventId == nonMatching.EventId);
            });
    }

    [Fact]
    public async Task GetRecentSummariesAsyncFiltersByBefore()
    {
        await WithArchiveDatabaseAsync(
            async (archive, query) =>
            {
                DateTimeOffset cursor =
                    new(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);

                IntegrationEventEnvelope earlier =
                    CreateEnvelope(
                        eventId: Guid.Parse("40000000-0000-0000-0000-000000000001"),
                        eventType: "agent.snapshot.captured",
                        source: "agent",
                        occurredAt: cursor.AddMinutes(-2),
                        receivedAt: cursor.AddMinutes(-2).AddSeconds(1));
                IntegrationEventEnvelope atCursor =
                    CreateEnvelope(
                        eventId: Guid.Parse("40000000-0000-0000-0000-000000000002"),
                        eventType: "agent.snapshot.captured",
                        source: "agent",
                        occurredAt: cursor,
                        receivedAt: cursor.AddSeconds(1));
                IntegrationEventEnvelope later =
                    CreateEnvelope(
                        eventId: Guid.Parse("40000000-0000-0000-0000-000000000003"),
                        eventType: "agent.snapshot.captured",
                        source: "agent",
                        occurredAt: cursor.AddMinutes(2),
                        receivedAt: cursor.AddMinutes(2).AddSeconds(1));

                await archive.StoreAsync(earlier);
                await archive.StoreAsync(atCursor);
                await archive.StoreAsync(later);

                IReadOnlyList<EventSummaryItem> summaries =
                    await query.GetRecentSummariesAsync(
                        10,
                        beforeOccurredAt: atCursor.OccurredAt,
                        beforeReceivedAt: atCursor.ReceivedAt,
                        beforeEventId: atCursor.EventId);

                var item = Assert.Single(summaries);
                Assert.Equal(earlier.EventId, item.EventId);
                Assert.True(item.OccurredAt < cursor);
            });
    }

    [Fact]
    public async Task GetRecentSummariesAsyncReturnsSummaryFields()
    {
        await WithArchiveDatabaseAsync(
            async (archive, query) =>
            {
                IntegrationEventEnvelope expected =
                    CreateEnvelope(
                        eventId: Guid.Parse("50000000-0000-0000-0000-000000000001"),
                        eventType: "github.push.received",
                        source: "github",
                        schemaVersion: 3,
                        occurredAt: new DateTimeOffset(
                            2026,
                            7,
                            15,
                            13,
                            15,
                            0,
                            TimeSpan.Zero),
                        receivedAt: new DateTimeOffset(
                            2026,
                            7,
                            15,
                            13,
                            15,
                            4,
                            TimeSpan.Zero),
                        correlationId: "corr-summary",
                        causationId: "cause-summary",
                        payload: new
                        {
                            repository = "JoyfulReaper/MissionControl",
                            branch = "feature-dashboard",
                            headSha = "abc123"
                        });

                await archive.StoreAsync(expected);

                IReadOnlyList<EventSummaryItem> summaries =
                    await query.GetRecentSummariesAsync(10);

                EventSummaryItem actual = Assert.Single(summaries);
                Assert.Equal(expected.EventId, actual.EventId);
                Assert.Equal(expected.EventType, actual.EventType);
                Assert.Equal(expected.Source, actual.Source);
                Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
                Assert.Equal(expected.OccurredAt, actual.OccurredAt);
                Assert.Equal(expected.ReceivedAt, actual.ReceivedAt);
                Assert.Equal(expected.CorrelationId, actual.CorrelationId);
                Assert.Equal(expected.CausationId, actual.CausationId);
            });
    }

    private static async Task WithArchiveDatabaseAsync(
        Func<SqliteEventArchive, SqliteEventQuery, Task> action)
    {
        string databasePath =
            Path.Combine(
                Path.GetTempPath(),
                $"mission-control-tests-{Guid.NewGuid():N}.db");

        try
        {
            await InitializeArchiveDatabaseAsync(databasePath);

            var connection = new SqliteEventArchiveConnection(
                $"Data Source={databasePath}");
            var archive = new SqliteEventArchive(connection);
            var query = new SqliteEventQuery(connection);

            await action(archive, query);
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    private static IntegrationEventEnvelope CreateEnvelope(
        Guid eventId,
        string eventType,
        string source,
        DateTimeOffset occurredAt,
        DateTimeOffset receivedAt,
        object? payload = null,
        int schemaVersion = 1,
        string? correlationId = "corr-default",
        string? causationId = "cause-default")
    {
        return new IntegrationEventEnvelope(
            EventId: eventId,
            EventType: eventType,
            Source: source,
            SchemaVersion: schemaVersion,
            OccurredAt: occurredAt,
            ReceivedAt: receivedAt,
            CorrelationId: correlationId,
            CausationId: causationId,
            Payload: JsonSerializer.SerializeToElement(
                payload ?? new
                {
                    eventId,
                    eventType,
                    source
                }));
    }

    private static async Task InitializeArchiveDatabaseAsync(
        string databasePath)
    {
        string schema = GetArchiveSchema();

        await using var connection = new SqliteConnection(
            $"Data Source={databasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = schema;
        await command.ExecuteNonQueryAsync();
    }

    private static string GetArchiveSchema()
    {
        Type schemaType =
            typeof(SqliteEventArchive).Assembly.GetType(
                "MissionControl.Archive.Storage.Sqlite.SqliteEventArchiveSchema")
            ?? throw new InvalidOperationException(
                "Archive schema type was not found.");

        return (string?)schemaType.GetField(
                "Sql",
                BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetRawConstantValue()
            ?? throw new InvalidOperationException(
                "Archive schema SQL was not found.");
    }
}
