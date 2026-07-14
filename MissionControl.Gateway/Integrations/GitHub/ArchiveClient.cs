using System.Text.Json;

namespace MissionControl.Gateway.Integrations.GitHub;

public class ArchiveClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<ArchivedEvent>> GetRecentGitPushesAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        string requestUri = $"/api/events?source=github" +
            $"&eventType=github.push.received" +
            $"&limit={limit}";

        return await httpClient.GetFromJsonAsync<List<ArchivedEvent>>(
            requestUri,
            cancellationToken
        ) ?? [];
    }
}

public sealed record ArchivedEvent(
    Guid EventId,
    string EventType,
    string Source,
    int SchemaVersion,
    DateTimeOffset OccurredAt,
    DateTimeOffset ReceivedAt,
    string? CorrelationId,
    string? CausationId,
    JsonElement Payload);