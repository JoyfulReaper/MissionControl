/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */

namespace MissionControl.Archive.Processing.RabbitMq;

internal static class RabbitMqTopology
{
    internal const string EventsExchange = "kgivler.events";
    internal const string ArchiveQueue = "mission-control.archive";
    internal const string AllEventsRoutingKey = "#";
}