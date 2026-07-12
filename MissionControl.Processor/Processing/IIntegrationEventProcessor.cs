using MissionControl.Contracts;

namespace MissionControl.Archive.Processing;

public interface IIntegrationEventProcessor
{
    Task ProcessAsync(
        IntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken = default);
}