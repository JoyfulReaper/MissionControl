/*
 * Mission Control
 *
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License
 */

namespace MissionControl.Messaging.RabbitMq;

public sealed class RabbitMqConsumerOptions
{
    public const string SectionName = "RabbitMqConsumer";

    public string ExchangeName { get; init; } =
        "kgivler.events";

    public string QueueName { get; init; } =
        string.Empty;

    public string RoutingKey { get; init; } =
        string.Empty;

    public ushort PrefetchCount { get; init; } = 10;
}