/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */

using MissionControl.Contracts.Archive;

namespace MissionControl.Client.Archive;

public interface IArchiveEventClient
{
    Task<IReadOnlyList<ArchiveEventSummaryItem>> GetRecentAsync(
        int limit = 50,
        string? source = null,
        string? eventType = null,
        ArchiveEventCursor? before = null,
        CancellationToken cancellationToken = default);

    Task<ArchiveEventDetailsItem?> GetByIdAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);

    Task<ArchiveStatisticsItem> GetStatisticsAsync(
        CancellationToken cancellationToken = default);
}