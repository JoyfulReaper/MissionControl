/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */

using Microsoft.Data.Sqlite;
using MissionControl.Archive.Contracts;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MissionControl.Archive.Storage.Sqlite;

public sealed class SqliteEventQuery(
    SqliteEventArchiveConnection database) : IIntegrationEventQuery
{
    private const int MaximumLimit = 200;

    public async Task<EventArchiveStatistics> GetStatisticsAsync(
        DateTimeOffset receivedSince,
        int topCategoryLimit = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            topCategoryLimit,
            1);

        topCategoryLimit = Math.Min(topCategoryLimit, 20);

        await using var connection =
            new SqliteConnection(database.ConnectionString);

        await connection.OpenAsync(cancellationToken);

        await using var summaryCommand = connection.CreateCommand();

        summaryCommand.CommandText =
            """
        SELECT
            COUNT(*),
            COUNT(
                CASE
                    WHEN ReceivedAt >= $receivedSince
                    THEN 1
                END),
            COUNT(DISTINCT Source),
            COUNT(DISTINCT EventType),
            MAX(ReceivedAt)
        FROM IntegrationEvents;
        """;

        summaryCommand.Parameters.AddWithValue(
            "$receivedSince",
            receivedSince
                .ToUniversalTime()
                .ToString("O"));

        long totalEvents;
        long recentlyReceivedEvents;
        long uniqueSources;
        long uniqueEventTypes;
        DateTimeOffset? latestReceivedAt;

        await using (var reader =
            await summaryCommand.ExecuteReaderAsync(
                cancellationToken))
        {
            bool hasResult =
                await reader.ReadAsync(cancellationToken);

            if (!hasResult)
            {
                throw new InvalidOperationException(
                    "The event statistics query returned no result.");
            }

            totalEvents = reader.GetInt64(0);
            recentlyReceivedEvents = reader.GetInt64(1);
            uniqueSources = reader.GetInt64(2);
            uniqueEventTypes = reader.GetInt64(3);

            latestReceivedAt = reader.IsDBNull(4)
                ? null
                : DateTimeOffset.Parse(reader.GetString(4));
        }

        IReadOnlyList<EventCategoryCount> topSources =
            await GetTopCategoriesAsync(
                connection,
                columnName: "Source",
                topCategoryLimit,
                cancellationToken);

        IReadOnlyList<EventCategoryCount> topEventTypes =
            await GetTopCategoriesAsync(
                connection,
                columnName: "EventType",
                topCategoryLimit,
                cancellationToken);

        return new EventArchiveStatistics(
            TotalEvents: totalEvents,
            EventsReceivedLast24Hours: recentlyReceivedEvents,
            UniqueSources: uniqueSources,
            UniqueEventTypes: uniqueEventTypes,
            LatestReceivedAt: latestReceivedAt,
            TopSources: topSources,
            TopEventTypes: topEventTypes);
    }

    public async Task<EventFeedItem?> GetByIdAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        await using var connection =
            new SqliteConnection(database.ConnectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText =
            """
        SELECT
            EventId,
            EventType,
            Source,
            SchemaVersion,
            OccurredAt,
            ReceivedAt,
            CorrelationId,
            CausationId,
            PayloadJson
        FROM IntegrationEvents
        WHERE EventId = $eventId
        LIMIT 1;
        """;

        command.Parameters.AddWithValue(
            "$eventId",
            eventId.ToString());

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadEvent(reader);
    }

    public async Task<IReadOnlyList<EventSummaryItem>>
        GetRecentSummariesAsync(
            int limit,
            string? source = null,
            string? eventType = null,
            DateTimeOffset? beforeOccurredAt = null,
            DateTimeOffset? beforeReceivedAt = null,
            Guid? beforeEventId = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        limit = Math.Min(limit, MaximumLimit);

        bool hasAnyCursorValue =
            beforeOccurredAt is not null ||
            beforeReceivedAt is not null ||
            beforeEventId is not null;

        bool hasCompleteCursor =
            beforeOccurredAt is not null &&
            beforeReceivedAt is not null &&
            beforeEventId is not null;

        if (hasAnyCursorValue && !hasCompleteCursor)
        {
            throw new ArgumentException(
                "All event cursor values must be provided together.");
        }

        await using var connection =
            new SqliteConnection(database.ConnectionString);

        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        var sql = new StringBuilder();

        sql.AppendLine(
            """
        SELECT
            EventId,
            EventType,
            Source,
            SchemaVersion,
            OccurredAt,
            ReceivedAt,
            CorrelationId,
            CausationId
        FROM IntegrationEvents
        WHERE 1 = 1
        """);

        if (!string.IsNullOrWhiteSpace(source))
        {
            sql.AppendLine("AND Source = $source");
            command.Parameters.AddWithValue("$source", source);
        }

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            sql.AppendLine("AND EventType = $eventType");

            command.Parameters.AddWithValue(
                "$eventType",
                eventType);
        }

        if (hasCompleteCursor)
        {
            sql.AppendLine(
                """
                AND
                (
                    OccurredAt < $beforeOccurredAt
                    OR
                    (
                        OccurredAt = $beforeOccurredAt
                        AND ReceivedAt < $beforeReceivedAt
                    )
                    OR
                    (
                        OccurredAt = $beforeOccurredAt
                        AND ReceivedAt = $beforeReceivedAt
                        AND EventId < $beforeEventId
                    )
                )
                """);

            command.Parameters.AddWithValue(
                "$beforeOccurredAt",
                beforeOccurredAt!.Value
                    .ToUniversalTime()
                    .ToString("O"));

            command.Parameters.AddWithValue(
                "$beforeReceivedAt",
                beforeReceivedAt!.Value
                    .ToUniversalTime()
                    .ToString("O"));

            command.Parameters.AddWithValue(
                "$beforeEventId",
                beforeEventId!.Value.ToString());
        }

        sql.AppendLine(
            """
        ORDER BY OccurredAt DESC, ReceivedAt DESC, EventId DESC
        LIMIT $limit;
        """);

        command.Parameters.AddWithValue(
            "$limit",
            limit);

        command.CommandText = sql.ToString();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        var events = new List<EventSummaryItem>();

        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(ReadEventSummary(reader));
        }

        return events;
    }

    private static EventSummaryItem ReadEventSummary(
        SqliteDataReader reader)
    {
        return new EventSummaryItem(
            EventId: Guid.Parse(reader.GetString(0)),
            EventType: reader.GetString(1),
            Source: reader.GetString(2),
            SchemaVersion: reader.GetInt32(3),
            OccurredAt: ParseTimestamp(reader.GetString(4)),
            ReceivedAt: ParseTimestamp(reader.GetString(5)),
            CorrelationId: reader.IsDBNull(6)
                ? null
                : reader.GetString(6),
            CausationId: reader.IsDBNull(7)
                ? null
                : reader.GetString(7));
    }

    public async Task<IReadOnlyList<EventFeedItem>> GetRecentAsync(
        int limit,
        string? source = null,
        string? eventType = null,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        limit = Math.Min(limit, MaximumLimit);

        await using var connection =
            new SqliteConnection(database.ConnectionString);

        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var sql = new StringBuilder();
        sql.AppendLine(
            """
            SELECT
                EventId,
                EventType,
                Source,
                SchemaVersion,
                OccurredAt,
                ReceivedAt,
                CorrelationId,
                CausationId,
                PayloadJson
            FROM IntegrationEvents
            WHERE 1 = 1
            """);

        if (!string.IsNullOrWhiteSpace(source))
        {
            sql.AppendLine("AND Source = $source");
            command.Parameters.AddWithValue("$source", source);
        }

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            sql.AppendLine("AND EventType = $eventType");

            command.Parameters.AddWithValue(
                "$eventType",
                eventType);
        }

        if (before is not null)
        {
            sql.AppendLine("AND OccurredAt < $before");

            command.Parameters.AddWithValue(
                "$before",
                before.Value
                    .ToUniversalTime()
                    .ToString("O"));
        }

        sql.AppendLine(
            """
            ORDER BY OccurredAt DESC, ReceivedAt DESC, EventId DESC
            LIMIT $limit;
            """);

        command.Parameters.AddWithValue(
            "$limit",
            limit);

        command.CommandText = sql.ToString();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        var events = new List<EventFeedItem>();

        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(ReadEvent(reader));
        }

        return events;
    }

    private static async Task<IReadOnlyList<EventCategoryCount>>
    GetTopCategoriesAsync(
        SqliteConnection connection,
        string columnName,
        int limit,
        CancellationToken cancellationToken)
    {
        if (columnName is not ("Source" or "EventType"))
        {
            throw new ArgumentException(
                "Unsupported event category column.",
                nameof(columnName));
        }

        await using var command = connection.CreateCommand();

        command.CommandText =
            $"""
        SELECT
            {columnName},
            COUNT(*) AS EventCount
        FROM IntegrationEvents
        GROUP BY {columnName}
        ORDER BY
            EventCount DESC,
            {columnName} ASC
        LIMIT $limit;
        """;

        command.Parameters.AddWithValue("$limit", limit);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        var results = new List<EventCategoryCount>();

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(
                new EventCategoryCount(
                    Name: reader.GetString(0),
                    Count: reader.GetInt64(1)));
        }

        return results;
    }

    private static EventFeedItem ReadEvent(SqliteDataReader reader)
    {
        var payloadJson = reader.GetString(8);

        using var payloadDocument = JsonDocument.Parse(payloadJson);

        return new EventFeedItem(
            EventId: Guid.Parse(reader.GetString(0)),
            EventType: reader.GetString(1),
            Source: reader.GetString(2),
            SchemaVersion: reader.GetInt32(3),
            OccurredAt: ParseTimestamp(reader.GetString(4)),
            ReceivedAt: ParseTimestamp(reader.GetString(5)),
            CorrelationId: reader.IsDBNull(6)
                ? null
                : reader.GetString(6),
            CausationId: reader.IsDBNull(7)
                ? null
                : reader.GetString(7),
            Payload: payloadDocument.RootElement.Clone()
        );
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        return DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
    }
}
