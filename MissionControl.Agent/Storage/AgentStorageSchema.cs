namespace MissionControl.Agent.Storage;

internal static class AgentStorageSchema
{
    internal const string Sql =
        """
        PRAGMA journal_mode = WAL;
        PRAGMA synchronous = NORMAL;

        CREATE TABLE IF NOT EXISTS NodeSnapshots
        (
            Node TEXT NOT NULL PRIMARY KEY,
            CapturedAt TEXT NOT NULL,
            Payload TEXT NOT NULL,
            PublishSucceeded INTEGER NULL,
            LastPublishAttemptAt TEXT NULL,
            UpdatedAt TEXT NOT NULL
        );
        """;
}