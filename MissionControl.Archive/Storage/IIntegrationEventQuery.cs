using MissionControl.Archive.Contracts;

namespace MissionControl.Archive.Storage;

public interface IIntegrationEventQuery
{
    Task<IReadOnlyList<EventFeedItem>> GetRecentAsync(
        int limit,
        string? source = null,
        string? eventType = null,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default);
}