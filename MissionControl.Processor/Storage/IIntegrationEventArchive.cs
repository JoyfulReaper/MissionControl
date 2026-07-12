using MissionControl.Contracts;

namespace MissionControl.Archive.Storage;

public interface IIntegrationEventArchive
{
    Task<bool> StoreAsync(
        IntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken = default);
}