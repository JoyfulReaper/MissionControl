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
            await httpClient.GetAsync($"/api/events?limit={limit}", cancellationToken);

        response.EnsureSuccessStatusCode();

        JsonElement? result =
            await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: cancellationToken);

        return result
            ?? throw new InvalidOperationException(
                "Archive returned an empty response.");
    }
}