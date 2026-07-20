/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */


using System.Text.Json;

namespace MissionControl.Contracts.Archive;

public sealed record ArchiveEventDetailsItem(
    Guid EventId,
    string EventType,
    string Source,
    int SchemaVersion,
    DateTimeOffset OccurredAt,
    DateTimeOffset ReceivedAt,
    string? CorrelationId,
    string? CausationId,
    JsonElement Payload);