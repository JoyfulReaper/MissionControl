/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */

namespace MissionControl.Contracts.Archive;

public sealed record ArchiveEventCursor(
    DateTimeOffset OccurredAt,
    DateTimeOffset ReceivedAt,
    Guid EventId);