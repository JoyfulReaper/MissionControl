using Microsoft.Data.Sqlite;
using MissionControl.Contracts;

namespace MissionControl.Archive.Storage.Sqlite;

public sealed class SqliteEventArchive(SqliteEventArchiveConnection database) : IIntegrationEventArchive
{
    public async Task<bool> StoreAsync(IntegrationEventEnvelope integrationEvent, CancellationToken cancellationToken = default)
    {
        await using var connection =
            new SqliteConnection(database.ConnectionString);

        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            INSERT INTO IntegrationEvents
            (
                EventId,
                EventType,
                Source,
                SchemaVersion,
                OccurredAt,
                ReceivedAt,
                CorrelationId,
                CausationId,
                PayloadJson,
                StoredAt
            )
            VALUES
            (
                $eventId,
                $eventType,
                $source,
                $schemaVersion,
                $occurredAt,
                $receivedAt,
                $correlationId,
                $causationId,
                $payloadJson,
                $storedAt
            )
            ON CONFLICT(EventId) DO NOTHING;
            """;

        command.Parameters.AddWithValue(
            "$eventId",
            integrationEvent.EventId.ToString());

        command.Parameters.AddWithValue(
            "$eventType",
            integrationEvent.EventType);

        command.Parameters.AddWithValue(
            "$source",
            integrationEvent.Source);

        command.Parameters.AddWithValue(
            "$schemaVersion",
            integrationEvent.SchemaVersion);

        command.Parameters.AddWithValue(
            "$occurredAt",
            integrationEvent.OccurredAt
                .ToUniversalTime()
                .ToString("O"));

        command.Parameters.AddWithValue(
            "$receivedAt",
            integrationEvent.ReceivedAt
                .ToUniversalTime()
                .ToString("O"));

        command.Parameters.AddWithValue(
            "$correlationId",
            (object?)integrationEvent.CorrelationId ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "$causationId",
            (object?)integrationEvent.CausationId ?? DBNull.Value);

        command.Parameters.AddWithValue(
            "$payloadJson",
            integrationEvent.Payload.GetRawText());

        command.Parameters.AddWithValue(
            "$storedAt",
            DateTimeOffset.UtcNow.ToString("O"));

        var affectedRows =
            await command.ExecuteNonQueryAsync(cancellationToken);

        return affectedRows == 1;
    }
}
