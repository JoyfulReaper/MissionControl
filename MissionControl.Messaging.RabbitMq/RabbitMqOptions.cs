/*
 * Mission Control
 *
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License
 */

namespace MissionControl.Messaging.RabbitMq;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; init; } = "localhost";

    public int Port { get; init; } = 5672;

    public string UserName { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string VirtualHost { get; init; } =
        "/mission-control";

    public string ClientProvidedName { get; init; } =
        "mission-control-consumer";
}