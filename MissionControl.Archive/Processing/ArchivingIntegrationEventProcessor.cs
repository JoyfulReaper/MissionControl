using MissionControl.Archive.Storage;
using MissionControl.Contracts;
using MissionControl.Messaging.RabbitMq;

namespace MissionControl.Archive.Processing;

public sealed class ArchivingIntegrationEventProcessor(
    IIntegrationEventArchive archive,
    ILogger<ArchivingIntegrationEventProcessor> logger)
    : IIntegrationEventProcessor
{
    public async Task ProcessAsync(
        IntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken = default)
    {
        var inserted = await archive.StoreAsync(
            integrationEvent,
            cancellationToken);

        if (inserted)
        {
            logger.LogInformation(
                "Archived event {EventType} from {Source} with ID {EventId}",
                integrationEvent.EventType,
                integrationEvent.Source,
                integrationEvent.EventId);
        }
        else
        {
            logger.LogInformation(
                "Ignored duplicate event {EventId}",
                integrationEvent.EventId);
        }
    }
}