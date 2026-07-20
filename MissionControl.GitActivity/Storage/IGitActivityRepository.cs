/*
 * Mission Control
 *
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License
 */

using MissionControl.Contracts.GitActivity;
using MissionControl.Contracts.GitHub;

namespace MissionControl.GitActivity.Storage;

public interface IGitActivityRepository
{
    Task UpsertPushAsync(
        Guid pushEventId,
        DateTimeOffset receivedAt,
        GitHubPushReceivedEvent push,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GitActivityItem>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken = default);
}
