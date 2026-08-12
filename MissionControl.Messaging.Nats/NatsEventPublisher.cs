using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MissionControl.Contracts;
using NATS.Client.JetStream;

namespace MissionControl.Messaging.Nats;

public sealed class NatsEventPublisher(
    INatsJSContext jetStream,
    IOptions<NatsOptions> options,
    ILogger<NatsEventPublisher> logger)
    : IEventPublisher
{
    private readonly NatsOptions _options = options.Value;

    public async Task PublishAsync(
        IntegrationEventEnvelope integrationEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);

        string subject = NatsSubjects.ForEventType(integrationEvent.EventType);

        var publishOptions = new NatsJSPubOpts
        {
            MsgId = integrationEvent.EventId.ToString(),
            ExpectedStream = _options.StreamName
        };

        var ack = await jetStream.PublishAsync(
            subject: subject,
            data: integrationEvent,
            opts: publishOptions,
            cancellationToken: cancellationToken);

        ack.EnsureSuccess();

        logger.LogDebug(
            "Published event {EventType} from {Source} with ID {EventId}",
            integrationEvent.EventType,
            integrationEvent.Source,
            integrationEvent.EventId);
    }
}