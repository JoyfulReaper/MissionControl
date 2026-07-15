using Dapper;
using MissionControl.Agent.Models;
using System.Globalization;
using System.Text.Json;

namespace MissionControl.Agent.Storage;

internal class SqliteNodeSnapshotStore(AgentDatabase database) : INodeSnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task SaveAsync(NodeSnapshotEvent snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        const string sql =
            """
            INSERT INTO NodeSnapshots
            (
                Node,
                CapturedAt,
                Payload,
                PublishSucceeded,
                LastPublishAttemptAt,
                UpdatedAt
            )
            VALUES
            (
                @Node,
                @CapturedAt,
                @Payload,
                NULL,
                NULL,
                @UpdatedAt
            )
            ON CONFLICT(Node) DO UPDATE SET
                CapturedAt = excluded.CapturedAt,
                Payload = excluded.Payload,
                PublishSucceeded = NULL,
                LastPublishAttemptAt = NULL,
                UpdatedAt = excluded.UpdatedAt;
            """;

        string capturedAt = snapshot.CapturedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        var parameters = new
        {
            snapshot.Node,
            CapturedAt = capturedAt,
            Payload = JsonSerializer.Serialize(snapshot, JsonOptions),
            UpdatedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)
        };

        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            parameters,
            cancellationToken: cancellationToken
        ));
    }

    public async Task RecordPublishResultAsync(
        string node,
        DateTimeOffset capturedAt,
        bool succeeded,
        DateTimeOffset attemptedAt,
        CancellationToken cancellationToken
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(node);

        const string sql =
            """
            UPDATE NodeSnapshots
            SET
                PublishSucceeded = @PublishSucceeded,
                LastPublishAttemptAt = @LastPublishAttemptAt,
                UpdatedAt = @UpdatedAt
            WHERE Node = @Node
              AND CapturedAt = @CapturedAt;
            """;

        var parameters = new
        {
            Node = node,
            CapturedAt = capturedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            PublishSucceeded = succeeded ? 1 : 0,
            LastPublishAttemptAt = attemptedAt
                .ToUniversalTime()
                .ToString("O", CultureInfo.InvariantCulture),
            UpdatedAt = DateTimeOffset.UtcNow.ToString(
                "O",
                CultureInfo.InvariantCulture)
        };

        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            new CommandDefinition
            (
                sql,
                parameters,
                cancellationToken: cancellationToken
            )
        );
    }

    public async Task<StoredNodeSnapshot?> GetAsync(
        string node,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(node);
        const string sql =
            """
            SELECT
                Payload,
                PublishSucceeded,
                LastPublishAttemptAt,
                UpdatedAt
            FROM NodeSnapshots
            WHERE Node = @Node
            LIMIT 1;
            """;

        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        NodeSnapshotRow? row =
            await connection.QuerySingleOrDefaultAsync<NodeSnapshotRow>(
                new CommandDefinition(
                    sql,
                    new { Node = node },
                    cancellationToken: cancellationToken));

        if (row is null)
            return null;

        NodeSnapshotEvent? snapshot =
            JsonSerializer.Deserialize<NodeSnapshotEvent>(row.Payload, JsonOptions);

        if (snapshot is null)
        {
            throw new InvalidOperationException(
                $"Stored snapshot for node '{node}' could not be deserialized.");
        }

        return new StoredNodeSnapshot(
            Snapshot: snapshot,
            PublishSucceeded: row.PublishSucceeded switch
            {
                null => null,
                0 => false,
                _ => true
            },
            LastPublishAttemptAt:
                ParseOptionalTimestamp(
                    row.LastPublishAttemptAt),
            UpdatedAt:
                ParseTimestamp(row.UpdatedAt));
    }

    private static DateTimeOffset ParseTimestamp(
        string value)
    {
        return DateTimeOffset.ParseExact(
            value,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
    }

    private static DateTimeOffset? ParseOptionalTimestamp(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : ParseTimestamp(value);
    }

    private sealed class NodeSnapshotRow
    {
        public required string Payload { get; init; }
        public long? PublishSucceeded { get; init; }
        public string? LastPublishAttemptAt { get; init; }
        public required string UpdatedAt { get; init; }
    }
}
