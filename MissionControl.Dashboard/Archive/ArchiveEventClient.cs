using System.Net;

namespace MissionControl.Dashboard.Archive;

public class ArchiveEventClient(HttpClient client)
    : IArchiveEventClient
{
    public async Task<IReadOnlyList<ArchiveEventSummaryItem>>
        GetRecentAsync(
            int limit = 50,
            CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 200);

        var events =
            await client.GetFromJsonAsync<ArchiveEventSummaryItem[]>(
                $"api/events/feed?limit={limit}",
                cancellationToken);

        return events ?? [];
    }

    public async Task<ArchiveEventDetailsItem?> GetByIdAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync(
            $"api/events/{eventId:D}",
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<ArchiveEventDetailsItem>(
                cancellationToken);
    }
}