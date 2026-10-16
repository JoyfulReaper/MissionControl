using System.Net.Http.Json;
using System.Text.Json;

namespace MissionControl.Mcp;

public sealed class ArchiveClient(HttpClient httpClient)
{
    public async Task<JsonElement> GetRecentEventsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 100);

        using HttpResponseMessage response =
            await httpClient.GetAsync(
                $"/api/events?limit={limit}",
                cancellationToken);

        response.EnsureSuccessStatusCode();

        JsonElement? result =
            await response.Content.ReadFromJsonAsync<JsonElement>(
                cancellationToken: cancellationToken);

        return result
            ?? throw new InvalidOperationException(
                "Archive returned an empty response.");
    }

    public async Task<JsonElement> GetRecentEventSummariesAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 100);

        using HttpResponseMessage response =
            await httpClient.GetAsync(
                $"/api/events/feed?limit={limit}",
                cancellationToken);

        response.EnsureSuccessStatusCode();

        JsonElement? result =
            await response.Content.ReadFromJsonAsync<JsonElement>(
                cancellationToken: cancellationToken);

        return result
            ?? throw new InvalidOperationException(
                "Archive returned an empty response.");
    }

    public async Task<JsonElement?> GetEventByIdAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await httpClient.GetAsync($"/api/events/{eventId:D}", cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);
    }

    public async Task<JsonElement> GetEventStatisticsAsync(
        int hours = 24,
        int top = 5,
        CancellationToken cancellationToken = default)
    {
        hours = Math.Clamp(hours, 1, 168);
        top = Math.Clamp(top, 1, 20);

        using HttpResponseMessage response =
            await httpClient.GetAsync(
                $"/api/events/statistics?hours={hours}&top={top}",
                cancellationToken);

        response.EnsureSuccessStatusCode();

        JsonElement? result =
            await response.Content.ReadFromJsonAsync<JsonElement>(
                cancellationToken: cancellationToken);

        return result
            ?? throw new InvalidOperationException(
                "Archive returned an empty statistics response.");
    }
}