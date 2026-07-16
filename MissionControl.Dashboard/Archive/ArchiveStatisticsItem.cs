namespace MissionControl.Dashboard.Archive;

public sealed record ArchiveStatisticsItem(
    long TotalEvents,
    long EventsReceivedLast24Hours,
    long UniqueSources,
    long UniqueEventTypes,
    DateTimeOffset? LatestReceivedAt,
    IReadOnlyList<ArchiveCategoryCountItem> TopSources,
    IReadOnlyList<ArchiveCategoryCountItem> TopEventTypes);