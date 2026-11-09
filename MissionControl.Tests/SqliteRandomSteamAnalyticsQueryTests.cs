
extern alias ArchiveApp;

using ArchiveApp::MissionControl.Archive.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using MissionControl.Contracts;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace MissionControl.Tests;

public sealed class SqliteRandomSteamAnalyticsQueryTests
{
    private const string Visit = "randomsteam.site-visit.recorded";
    private const string Pick = "randomsteam.game-pick.completed";
    private const string Export = "randomsteam.library-export.completed";

    static SqliteRandomSteamAnalyticsQueryTests()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    [Fact]
    public async Task Overview_CountsEventsAndDistinctVisitorsWithinWindow()
    {
        await WithDatabaseAsync(async (archive, query) =>
        {
            var from = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
            var to = from.AddDays(1);

            async Task Add(string eventType, DateTimeOffset occurredAt, object payload)
            {
                var envelope = new IntegrationEventEnvelope(
                    EventId: Guid.NewGuid(),
                    EventType: eventType,
                    Source: "randomsteam",
                    SchemaVersion: 1,
                    OccurredAt: occurredAt,
                    ReceivedAt: occurredAt,
                    CorrelationId: null,
                    CausationId: null,
                    Payload: JsonSerializer.SerializeToElement(payload));

                Assert.True(await archive.StoreAsync(envelope));
            }

            await Add(Visit, from, new { visitorId = "visitor-a" });
            await Add(Visit, from.AddHours(1), new { visitorId = "visitor-a" });
            await Add(Visit, from.AddHours(2), new { visitorId = "visitor-b" });
            await Add(Visit, from.AddHours(3), new { visitorId = (string?)null });

            await Add(Pick, from.AddHours(4), new { succeeded = true });
            await Add(Pick, from.AddHours(5), new { succeeded = false });

            await Add(Export, from.AddHours(6), new { gameCount = 100 });

            // Events outside the requested interval must not count.
            await Add(Pick, from.AddMinutes(-1), new { succeeded = true });
            await Add(Visit, to, new { visitorId = "visitor-c" });

            // Unrelated event types must not affect the overview.
            await Add("other.event", from.AddHours(7), new { succeeded = true });

            var result = await query.GetOverviewAsync(from, to);

            Assert.Equal(from, result.FromUtc);
            Assert.Equal(to, result.ToUtc);
            Assert.Equal(4, result.HomepageVisits);
            Assert.Equal(2, result.IdentifiedVisitors);
            Assert.Equal(2, result.PickerAttempts);
            Assert.Equal(1, result.SuccessfulPicks);
            Assert.Equal(1, result.FailedPicks);
            Assert.Equal(1, result.CompletedExports);
        });
    }

    [Fact]
    public async Task Overview_EmptyDatabaseReturnsZeroCounts()
    {
        await WithDatabaseAsync(async (_, query) =>
        {
            var from = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
            var result = await query.GetOverviewAsync(from, from.AddDays(1));

            Assert.Equal(0, result.HomepageVisits);
            Assert.Equal(0, result.IdentifiedVisitors);
            Assert.Equal(0, result.PickerAttempts);
            Assert.Equal(0, result.SuccessfulPicks);
            Assert.Equal(0, result.FailedPicks);
            Assert.Equal(0, result.CompletedExports);
        });
    }

    [Fact]
    public async Task Overview_RejectsInvalidDateRange()
    {
        await WithDatabaseAsync(async (_, query) =>
        {
            var date = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => query.GetOverviewAsync(date, date));

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => query.GetOverviewAsync(date.AddDays(1), date));
        });
    }

    private static async Task WithDatabaseAsync(
        Func<SqliteEventArchive, SqliteRandomSteamAnalyticsQuery, Task> action)
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"randomsteam-analytics-tests-{Guid.NewGuid():N}.db");

        try
        {
            var schemaType = typeof(SqliteEventArchive).Assembly.GetType(
                "MissionControl.Archive.Storage.Sqlite.SqliteEventArchiveSchema")
                ?? throw new InvalidOperationException("Archive schema not found.");

            var schema = (string?)schemaType.GetField(
                "Sql", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetRawConstantValue()
                ?? throw new InvalidOperationException("Archive schema SQL not found.");

            var connectionString = $"Data Source={databasePath}";

            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();

                await using var command = connection.CreateCommand();
                command.CommandText = schema;
                await command.ExecuteNonQueryAsync();
            }

            var database = new SqliteEventArchiveConnection(connectionString);
            var archive = new SqliteEventArchive(database);
            var query = new SqliteRandomSteamAnalyticsQuery(database);

            await action(archive, query);
        }
        finally
        {
            SqliteConnection.ClearAllPools();

            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }
}
