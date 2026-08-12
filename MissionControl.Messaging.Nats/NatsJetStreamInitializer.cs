using Microsoft.Extensions.Options;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace MissionControl.Messaging.Nats;

public sealed class NatsJetStreamInitializer(
    INatsJSContext jetStream,
    IOptions<NatsOptions> options)
{
    private readonly NatsOptions _options = options.Value;

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        var config = new StreamConfig(_options.StreamName, [NatsSubjects.AllEvents])
        {
            Description = "Mission Control integration events"
        };

        await jetStream.CreateOrUpdateStreamAsync(
            config,
            cancellationToken);
    }
}