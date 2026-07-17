using MissionControl.Agent.Models;

namespace MissionControl.Agent.Host;

internal interface IHostMetricsCollector
{
    Task<HostMetric> GetMetricsAsync(
        CancellationToken cancellationToken = default);
}
