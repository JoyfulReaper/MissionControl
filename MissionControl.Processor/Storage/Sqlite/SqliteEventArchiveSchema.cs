namespace MissionControl.Processor.Storage.Sqlite;

internal static class SqliteEventArchiveSchema
{
    internal const string Sql =
        """
        CREATE TABLE IF NOT EXISTS IntegrationEvents
        (
            EventId       TEXT NOT NULL PRIMARY KEY,
            EventType     TEXT NOT NULL,
            Source        TEXT NOT NULL,
            SchemaVersion INTEGER NOT NULL,
            OccurredAt    TEXT NOT NULL,
            ReceivedAt    TEXT NOT NULL,
            CorrelationId TEXT NULL,
            CausationId   TEXT NULL,
            PayloadJson   TEXT NOT NULL,
            StoredAt      TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS IX_IntegrationEvents_OccurredAt
            ON IntegrationEvents (OccurredAt DESC);

        CREATE INDEX IF NOT EXISTS IX_IntegrationEvents_Source_OccurredAt
            ON IntegrationEvents (Source, OccurredAt DESC);

        CREATE INDEX IF NOT EXISTS IX_IntegrationEvents_EventType_OccurredAt
            ON IntegrationEvents (EventType, OccurredAt DESC);
        """;
}