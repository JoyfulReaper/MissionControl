using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace MissionControl.Messaging.Nats;

public sealed class NatsJetStreamInitializer(
    INatsJSContext jetStream,
    IOptions<NatsOptions> options)
    : IHostedService
{

    private const long MaxStreamBytes = 256L * 1024 * 1024;
    private static readonly TimeSpan MaxStreamAge = TimeSpan.FromDays(7);

    private readonly NatsOptions _options = options.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var config = new StreamConfig(
            _options.StreamName,
            [NatsSubjects.AllEvents])
        {
            Description = "Mission Control integration events",
            Storage = StreamConfigStorage.File,
            Retention = StreamConfigRetention.Limits,
            Discard = StreamConfigDiscard.Old,
            MaxAge = MaxStreamAge,
            MaxBytes = MaxStreamBytes,
            NumReplicas = 1
        };

        await jetStream.CreateOrUpdateStreamAsync(config, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}