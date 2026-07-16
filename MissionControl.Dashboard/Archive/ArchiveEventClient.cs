namespace MissionControl.Dashboard.Archive;

public class ArchiveEventClient(HttpClient client) : IArchiveEventClient
{
    public async Task<IReadOnlyList<ArchiveEventSummaryItem>> GetRecentAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 200);

        var events = await client.GetFromJsonAsync<ArchiveEventSummaryItem[]>(
            $"api/events/feed?limit={limit}",
            cancellationToken
        );

        return events ?? [];
    }
}
