using System.Text.Json;

namespace MissionControl.Contracts;

public sealed record IntegrationEventEnvelope(
    Guid EventId,
    string EventType,
    string Source,
    int SchemaVersion,
    DateTimeOffset OccurredAt,
    DateTimeOffset ReceivedAt,
    string? CorrelationId,
    string? CausationId,
    JsonElement Payload
    );
