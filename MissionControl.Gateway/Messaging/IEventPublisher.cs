using MissionControl.Contracts;

namespace MissionControl.Gateway.Messaging;

public interface IEventPublisher
{
    Task PublishAsync(
        IntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken = default);
}