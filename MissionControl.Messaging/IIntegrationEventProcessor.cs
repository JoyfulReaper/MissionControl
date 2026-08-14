using MissionControl.Contracts;

namespace MissionControl.Messaging;

public interface IIntegrationEventProcessor
{
    Task ProcessAsync(
        IntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken = default);
}