using MissionControl.Contracts;

namespace MissionControl.Processor.Storage;

public interface IIntegrationEventArchive
{
    Task<bool> StoreAsync(
        IntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken = default);
}