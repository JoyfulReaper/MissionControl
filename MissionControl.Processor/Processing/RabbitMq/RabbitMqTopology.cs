/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */

namespace MissionControl.Processor.Processing.RabbitMq;

internal static class RabbitMqTopology
{
    internal const string EventsExchange = "kgivler.events";
    internal const string ProcessorQueue = "mission-control.processor";
    internal const string AllEventsRoutingKey = "#";
}