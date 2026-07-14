/*
 * Mission Control
 *
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License
 */

namespace MissionControl.Contracts.GitHub;

public sealed record GitHubPushReceivedEvent(
    string Repository,
    long RepositoryId,
    string RepositoryUrl,
    string Branch,
    string Ref,
    string Before,
    string After,
    bool Created,
    bool Forced,
    string? Pusher,
    string Sender,
    string? CompareUrl,
    int CommitCount,
    IReadOnlyList<GitHubCommitSummary> Commits);

public sealed record GitHubCommitSummary(
    string Sha,
    string Message,
    string? Author,
    string? AuthorUsername,
    DateTimeOffset Timestamp,
    string Url);