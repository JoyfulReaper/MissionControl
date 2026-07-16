/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */

namespace MissionControl.Dashboard.Archive;

public sealed record ArchiveEventCursor(
    DateTimeOffset OccurredAt,
    DateTimeOffset ReceivedAt,
    Guid EventId);