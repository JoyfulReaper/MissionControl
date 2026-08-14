using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MissionControl.Messaging.Nats;

namespace MissionControl.Observability.Nats;

public sealed class NatsConsumerHealthCheck(
    NatsConsumerStatus status,
    IOptions<NatsConsumerOptions> options)
    : IHealthCheck
{
    private readonly NatsConsumerOptions _options = options.Value;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = status.IsRunning
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy(
                $"NATS consumer '{_options.DurableName}' is not running.");

        return Task.FromResult(result);
    }
}