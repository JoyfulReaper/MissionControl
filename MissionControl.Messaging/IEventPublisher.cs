using MissionControl.Contracts;

namespace MissionControl.Messaging;

public interface IEventPublisher
{
    Task PublishAsync(
        IntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken = default);
}