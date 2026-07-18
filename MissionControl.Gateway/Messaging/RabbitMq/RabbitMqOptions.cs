/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */

namespace MissionControl.Gateway.Messaging.RabbitMq;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public required string HostName { get; init; } = string.Empty;

    public int Port { get; init; } = 5672;

    public required string UserName { get; init; } = string.Empty;

    public required string Password { get; init; } = string.Empty;

    public string VirtualHost { get; init; } = string.Empty;

    public string ClientProvidedName { get; init; } =
        string.Empty;
}
