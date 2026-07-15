using Microsoft.Extensions.Options;
using MissionControl.Agent.Docker;
using MissionControl.Agent.Models;

namespace MissionControl.Agent;

public sealed class AgentWorker(
    ILogger<AgentWorker> logger,
    IDockerMetricsCollector dockerMetricsCollector,
    IOptions<AgentOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        var agentOptions = options.Value;

        logger.LogInformation(
            "Mission Control Agent started for node {NodeName}.",
            agentOptions.NodeName);

        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(
                agentOptions.IntervalSeconds));

        do
        {
            IReadOnlyList<ContainerMetric> containers =
                await dockerMetricsCollector.GetMetricsAsync(
                    stoppingToken);

            logger.LogInformation(
                "Collected metrics for {ContainerCount} containers on {NodeName}.",
                containers.Count,
                agentOptions.NodeName);

            foreach (var container in containers)
            {
                logger.LogInformation(
                    "{Container}: {MemoryUsageBytes} / {MemoryLimitBytes} bytes ({MemoryPercent:F1}%).",
                    container.Name,
                    container.MemoryUsageBytes,
                    container.MemoryLimitBytes,
                    container.MemoryPercent);
            }
        }
        while (await timer.WaitForNextTickAsync(
                   stoppingToken));
    }
}