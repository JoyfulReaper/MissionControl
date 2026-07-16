namespace MissionControl.Dashboard.Archive;

public interface IArchiveEventClient
{
    Task<IReadOnlyList<ArchiveEventFeedItem>> GetRecentAsync(
        int limit = 50,
        CancellationToken cancellationToken = default);
}