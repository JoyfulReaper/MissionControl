extern alias ArchiveApp;
using ArchiveApp::MissionControl.Archive.Processing;
using ArchiveApp::MissionControl.Archive.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using MissionControl.Contracts;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace MissionControl.Tests;

public sealed class GatewayArchiveCompatibilityTests
{
    [Fact]
    public async Task GitHubGatewayEnvelopeCanBeStoredAndDeduplicatedByArchive()
    {
        SQLitePCL.Batteries_V2.Init();

        IntegrationEventEnvelope gatewayEnvelope =
            await ProduceGitHubEnvelopeAsync();

        byte[] serializedEnvelope =
            JsonSerializer.SerializeToUtf8Bytes(gatewayEnvelope);

        var archiveEnvelope =
            JsonSerializer.Deserialize<IntegrationEventEnvelope>(
                serializedEnvelope,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(archiveEnvelope);

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
            var processor = new ArchivingIntegrationEventProcessor(
                archive,
                NullLogger<ArchivingIntegrationEventProcessor>.Instance);
            var query = new SqliteEventQuery(connection);

            await processor.ProcessAsync(archiveEnvelope!);

            var stored = await query.GetRecentAsync(10);
            var item = Assert.Single(stored);
            Assert.Equal("github", item.Source);
            Assert.Equal("github.push.received", item.EventType);
            Assert.Equal(gatewayEnvelope.EventId, item.EventId);
            Assert.Equal(gatewayEnvelope.CorrelationId, item.CorrelationId);
            Assert.Equal(
                "JoyfulReaper/MissionControl",
                item.Payload.GetProperty("repository").GetString());
            Assert.Equal("dev", item.Payload.GetProperty("branch").GetString());
            Assert.Equal(
                "1111111111111111111111111111111111111111",
                item.Payload.GetProperty("commits")[0].GetProperty("sha").GetString());
            Assert.Equal(
                "First commit line",
                item.Payload.GetProperty("commits")[0].GetProperty("message").GetString());

            await processor.ProcessAsync(archiveEnvelope);

            var afterDuplicate = await query.GetRecentAsync(10);
            Assert.Single(afterDuplicate);
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

    private static async Task<IntegrationEventEnvelope> ProduceGitHubEnvelopeAsync()
    {
        await using var factory = new GatewayTestApplicationFactory();
        using var client = factory.CreateClient();
        using var response = await client.SendAsync(
            GitHubTestPayloads.SignedGitHubRequest(
                "push",
                GitHubTestPayloads.PushBytes()));
        response.EnsureSuccessStatusCode();

        return Assert.Single(factory.Publisher.Events);
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
