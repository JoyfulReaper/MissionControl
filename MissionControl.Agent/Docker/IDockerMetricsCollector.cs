using MissionControl.Agent.Models;

namespace MissionControl.Agent.Docker;

public interface IDockerMetricsCollector
{
    Task<IReadOnlyList<ContainerMetric>> GetMetricsAsync(
        CancellationToken cancellationToken = default);
}