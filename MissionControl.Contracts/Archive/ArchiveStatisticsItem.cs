namespace MissionControl.Contracts.Archive;

public sealed record ArchiveStatisticsItem(
    long TotalEvents,
    long EventsReceivedInWindow,
    long UniqueSources,
    long UniqueEventTypes,
    DateTimeOffset? LatestReceivedAt,
    IReadOnlyList<ArchiveCategoryCountItem> TopSources,
    IReadOnlyList<ArchiveCategoryCountItem> TopEventTypes);