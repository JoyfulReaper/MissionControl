using MissionControl.Agent.Models;

namespace MissionControl.Agent.Docker;

public interface IDockerMetricsCollector
{
    Task<DockerMetricsCollectionResult> GetMetricsAsync(
        CancellationToken cancellationToken = default);
}
