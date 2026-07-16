/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */


using MissionControl.Archive.Contracts;

namespace MissionControl.Archive.Storage;

public interface IIntegrationEventQuery
{
    Task<EventFeedItem?> GetByIdAsync(
        Guid eventId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EventFeedItem>> GetRecentAsync(
        int limit,
        string? source = null,
        string? eventType = null,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EventSummaryItem>> GetRecentSummariesAsync(
        int limit,
        string? source = null,
        string? eventType = null,
        DateTimeOffset? beforeOccurredAt = null,
        DateTimeOffset? beforeReceivedAt = null,
        Guid? beforeEventId = null,
        CancellationToken cancellationToken = default);
}