/*
 * Mission Control
 *
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License
 */

namespace MissionControl.GitActivity.Messaging.RabbitMq;

internal static class RabbitMqTopology
{
    internal const string EventsExchange = "kgivler.events";

    internal const string GitActivityQueue =
        "mission-control.git-activity";

    internal const string GitHubPushRoutingKey =
        "github.push.received";
}