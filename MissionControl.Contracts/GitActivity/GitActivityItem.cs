/*
 * Mission Control
 *
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License
 */

namespace MissionControl.Contracts.GitActivity;

public sealed record GitActivityItem(
    string Repository,
    string Branch,
    string Sha,
    string Message,
    string? Author,
    string? AuthorUsername,
    DateTimeOffset Timestamp,
    string Url);
