using System.Text.Json;

namespace MissionControl.Dashboard.Archive;

public sealed record ArchiveEventFeedItem(
    Guid EventId,
    string EventType,
    string Source,
    int SchemaVersion,
    DateTimeOffset OccurredAt,
    DateTimeOffset ReceivedAt,
    string? CorrelationId,
    string? CausationId,
    JsonElement Payload);