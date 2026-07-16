using Microsoft.AspNetCore.WebUtilities;
using System.Globalization;
using System.Net;

namespace MissionControl.Dashboard.Archive;

public class ArchiveEventClient(HttpClient client)
    : IArchiveEventClient
{
    public async Task<IReadOnlyList<ArchiveEventSummaryItem>>
        GetRecentAsync(
            int limit = 50,
            string? source = null,
            string? eventType = null,
            CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 200);

        var queryParameters = new Dictionary<string, string?>
        {
            ["limit"] =
                limit.ToString(CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(source))
        {
            queryParameters["source"] = source.Trim();
        }

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            queryParameters["eventType"] = eventType.Trim();
        }

        string requestUri = QueryHelpers.AddQueryString(
            "api/events/feed",
            queryParameters);

        var events =
            await client.GetFromJsonAsync<ArchiveEventSummaryItem[]>(
                requestUri,
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