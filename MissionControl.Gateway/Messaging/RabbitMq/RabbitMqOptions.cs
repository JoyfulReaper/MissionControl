/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */

namespace Kgivler.MissionControl.Gateway.Messaging.RabbitMq;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public required string HostName { get; init; } = "localhost";

    public int Port { get; init; } = 5672;

    public required string UserName { get; init; } = string.Empty;

    public required string Password { get; init; } = string.Empty;

    public string VirtualHost { get; init; } = "/mission-control";

    public string ClientProvidedName { get; init; } =
        "mission-control-gateway";
}