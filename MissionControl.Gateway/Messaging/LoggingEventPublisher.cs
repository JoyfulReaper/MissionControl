using MissionControl.Contracts;

namespace MissionControl.Gateway.Messaging;

public sealed class LoggingEventPublisher(ILogger<LoggingEventPublisher> logger)
    : IEventPublisher
{
    public Task PublishAsync(IntegrationEventEnvelope integrationEvent, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Published event {EventType} from {Source} with ID {EventId}, version {SchemaVersion}",
            integrationEvent.EventType,
            integrationEvent.Source,
            integrationEvent.EventId,
            integrationEvent.SchemaVersion);

        return Task.CompletedTask;
    }
}