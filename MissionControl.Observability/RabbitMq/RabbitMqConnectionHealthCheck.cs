using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace MissionControl.Observability.RabbitMq;

public sealed class RabbitMqConnectionHealthCheck(
    IRabbitMqConnectionStatus connectionStatus)
    : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var snapshot = connectionStatus.GetSnapshot();

        var data = new Dictionary<string, object>
        {
            ["connectionOpen"] = snapshot.ConnectionOpen,
            ["channelOpen"] = snapshot.ChannelOpen
        };

        var result = snapshot.IsConnected
            ? HealthCheckResult.Healthy(
                "RabbitMQ connection and channel are open.",
                data)
            : HealthCheckResult.Unhealthy(
                "RabbitMQ connection or channel is not open.",
                data: data);

        return Task.FromResult(result);
    }
}