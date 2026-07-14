/*
 * Mission Control
 *
 * Copyright (c) 2026 Kyle Givler
 * Licensed under the MIT License
 */

using MissionControl.Contracts;

namespace MissionControl.GitActivity.Processing;

public interface IIntegrationEventProcessor
{
    Task ProcessAsync(
        IntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken = default);
}