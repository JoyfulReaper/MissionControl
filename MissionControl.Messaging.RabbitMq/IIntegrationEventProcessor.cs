using MissionControl.Contracts;

namespace MissionControl.Messaging.RabbitMq;

public interface IIntegrationEventProcessor
{
    Task ProcessAsync(
        IntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken = default);
}