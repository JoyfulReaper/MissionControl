using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MissionControl.Messaging.Nats;
using NATS.Client.JetStream;

namespace MissionControl.Observability.Nats;

public sealed class NatsJetStreamHealthCheck(
    INatsJSContext jetStream,
    IOptions<NatsOptions> options)
    : IHealthCheck
{
    private readonly NatsOptions _options = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await jetStream.GetStreamAsync(
                _options.StreamName,
                cancellationToken: cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                $"NATS JetStream stream '{_options.StreamName}' is unavailable.",
                exception);
        }
    }
}