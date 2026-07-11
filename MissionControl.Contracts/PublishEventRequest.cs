using System.Text.Json;

namespace MissionControl.Contracts;

public sealed record PublishEventRequest(
    Guid EventId,
    string EventType,
    int SchemaVersion,
    DateTimeOffset OccurredAt,
    string? CorrelationId,
    JsonElement Payload);

public sealed record PublishEventAcceptedResponse(Guid EventId);