using MissionControl.Contracts;

namespace MissionControl.Processor.Processing;

public interface IIntegrationEventProcessor
{
    Task ProcessAsync(
        IntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken = default);
}