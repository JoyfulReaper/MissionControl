/*
 * Mission Control
 *
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License
 */

using Microsoft.Data.Sqlite;
using MissionControl.Contracts.GitHub;
using MissionControl.GitActivity.Contracts;
using System.Globalization;

namespace MissionControl.GitActivity.Storage.Sqlite;

public sealed class SqliteGitActivityRepository(
    GitActivityConnection database)
    : IGitActivityRepository
{
    public async Task UpsertPushAsync(
        Guid pushEventId,
        DateTimeOffset receivedAt,
        GitHubPushReceivedEvent push,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(push);

        if (push.Commits.Count == 0)
        {
            return;
        }

        await using var connection =
            new SqliteConnection(database.ConnectionString);

        await connection.OpenAsync(cancellationToken);

        using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO GitActivityItems
            (
                Repository,
                Branch,
                Sha,
                Message,
                Author,
                AuthorUsername,
                TimestampUtc,
                Url,
                PushEventId,
                ReceivedAtUtc
            )
            VALUES
            (
                $repository,
                $branch,
                $sha,
                $message,
                $author,
                $authorUsername,
                $timestampUtc,
                $url,
                $pushEventId,
                $receivedAtUtc
            )
            ON CONFLICT (Repository, Sha)
            DO UPDATE SET
                Branch = excluded.Branch,
                Message = excluded.Message,
                Author = excluded.Author,
                AuthorUsername = excluded.AuthorUsername,
                TimestampUtc = excluded.TimestampUtc,
                Url = excluded.Url,
                PushEventId = excluded.PushEventId,
                ReceivedAtUtc = excluded.ReceivedAtUtc
            WHERE excluded.ReceivedAtUtc >=
                GitActivityItems.ReceivedAtUtc;
            """;

        var repositoryParameter =
            command.Parameters.Add("$repository", SqliteType.Text);

        var branchParameter =
            command.Parameters.Add("$branch", SqliteType.Text);

        var shaParameter =
            command.Parameters.Add("$sha", SqliteType.Text);

        var messageParameter =
            command.Parameters.Add("$message", SqliteType.Text);

        var authorParameter =
            command.Parameters.Add("$author", SqliteType.Text);

        var authorUsernameParameter =
            command.Parameters.Add("$authorUsername", SqliteType.Text);

        var timestampParameter =
            command.Parameters.Add("$timestampUtc", SqliteType.Text);

        var urlParameter =
            command.Parameters.Add("$url", SqliteType.Text);

        var pushEventIdParameter =
            command.Parameters.Add("$pushEventId", SqliteType.Text);

        var receivedAtParameter =
            command.Parameters.Add("$receivedAtUtc", SqliteType.Text);

        foreach (var commit in push.Commits)
        {
            repositoryParameter.Value = push.Repository;
            branchParameter.Value = push.Branch;
            shaParameter.Value = commit.Sha;
            messageParameter.Value = commit.Message;

            authorParameter.Value =
                (object?)commit.Author ?? DBNull.Value;

            authorUsernameParameter.Value =
                (object?)commit.AuthorUsername ?? DBNull.Value;

            timestampParameter.Value = commit.Timestamp
                .ToUniversalTime()
                .ToString("O");

            urlParameter.Value = commit.Url;
            pushEventIdParameter.Value = pushEventId.ToString();

            receivedAtParameter.Value = receivedAt
                .ToUniversalTime()
                .ToString("O");

            await command.ExecuteNonQueryAsync(
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<GitActivityItem>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        await using var connection =
            new SqliteConnection(database.ConnectionString);

        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT
                Repository,
                Branch,
                Sha,
                Message,
                Author,
                AuthorUsername,
                TimestampUtc,
                Url
            FROM GitActivityItems
            ORDER BY
                TimestampUtc DESC,
                Repository ASC,
                Sha ASC
            LIMIT $limit;
            """;

        command.Parameters.AddWithValue("$limit", limit);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        var items = new List<GitActivityItem>();

        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(
                new GitActivityItem(
                    Repository: reader.GetString(0),
                    Branch: reader.GetString(1),
                    Sha: reader.GetString(2),
                    Message: reader.GetString(3),
                    Author: reader.IsDBNull(4)
                        ? null
                        : reader.GetString(4),
                    AuthorUsername: reader.IsDBNull(5)
                        ? null
                        : reader.GetString(5),
                    Timestamp: ParseTimestamp(
                        reader.GetString(6)),
                    Url: reader.GetString(7)));
        }

        return items;
    }

    private static DateTimeOffset ParseTimestamp(string value)
    {
        return DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
    }
}