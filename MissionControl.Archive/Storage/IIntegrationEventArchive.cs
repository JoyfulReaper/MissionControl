/*
 * Mission Control
 * Copyright 2026 Kyle Givler
 * Licensed under the MIT License
 */


using MissionControl.Contracts;

namespace MissionControl.Archive.Storage;

public interface IIntegrationEventArchive
{
    Task<bool> StoreAsync(
        IntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken = default);
}