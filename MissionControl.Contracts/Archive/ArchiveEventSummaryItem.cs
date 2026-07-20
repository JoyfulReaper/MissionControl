/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */

namespace MissionControl.Contracts.Archive;

public sealed record ArchiveEventSummaryItem(
    Guid EventId,
    string EventType,
    string Source,
    int SchemaVersion,
    DateTimeOffset OccurredAt,
    DateTimeOffset ReceivedAt,
    string? CorrelationId,
    string? CausationId);