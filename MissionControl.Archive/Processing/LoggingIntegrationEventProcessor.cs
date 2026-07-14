using MissionControl.Contracts;
using MissionControl.Messaging.RabbitMq;

namespace MissionControl.Archive.Processing;

public sealed class LoggingIntegrationEventProcessor(
    ILogger<LoggingIntegrationEventProcessor> logger)
    : IIntegrationEventProcessor
{
    public Task ProcessAsync(
        IntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Processed event {EventType} from {Source} with ID {EventId}. Payload: {Payload}",
            integrationEvent.EventType,
            integrationEvent.Source,
            integrationEvent.EventId,
            integrationEvent.Payload);

        return Task.CompletedTask;
    }
}