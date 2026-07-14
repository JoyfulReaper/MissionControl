/*
 * Mission Control
 *
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License
 */

namespace MissionControl.GitActivity.Storage.Sqlite;

internal static class GitActivitySchema
{
    internal const string Sql =
        """
        CREATE TABLE IF NOT EXISTS GitActivityItems
        (
            Repository      TEXT NOT NULL,
            Branch          TEXT NOT NULL,
            Sha             TEXT NOT NULL,
            Message         TEXT NOT NULL,
            Author          TEXT NULL,
            AuthorUsername  TEXT NULL,
            TimestampUtc    TEXT NOT NULL,
            Url             TEXT NOT NULL,
            PushEventId     TEXT NOT NULL,
            ReceivedAtUtc   TEXT NOT NULL,

            PRIMARY KEY (Repository, Sha)
        );

        CREATE INDEX IF NOT EXISTS IX_GitActivityItems_TimestampUtc
            ON GitActivityItems (TimestampUtc DESC);
        """;
}