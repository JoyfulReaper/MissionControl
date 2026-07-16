namespace MissionControl.Dashboard.Archive;

public interface IArchiveEventClient
{
    Task<IReadOnlyList<ArchiveEventSummaryItem>> GetRecentAsync(
        int limit = 50,
        CancellationToken cancellationToken = default);
}