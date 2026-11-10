/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */

namespace MissionControl.Archive.Contracts;

public sealed record EventArchiveStatistics(
    long TotalEvents,
    long EventsReceivedInWindow,
    long UniqueSources,
    long UniqueEventTypes,
    DateTimeOffset? LatestReceivedAt,
    IReadOnlyList<EventCategoryCount> TopSources,
    IReadOnlyList<EventCategoryCount> TopEventTypes)
{
    // Compatibility with older Dashboard deployments.
    public long EventsReceivedLast24Hours => EventsReceivedInWindow;
}