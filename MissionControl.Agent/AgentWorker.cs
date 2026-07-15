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

        if (!agentOptions.DockerEnabled)
        {
            logger.LogWarning(
                "Docker metric collection is disabled on {OperatingSystem}.",
                Environment.OSVersion.Platform);
        }

        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(
                agentOptions.IntervalSeconds));

        do
        {
            if (agentOptions.DockerEnabled)
            {
                await CollectDockerMetricsAsync(
                    agentOptions,
                    stoppingToken);
            }

            // Protocol probes will run here later,
            // regardless of whether Docker metrics are enabled.
        }
        while (await timer.WaitForNextTickAsync(
                   stoppingToken));
    }

    private async Task CollectDockerMetricsAsync(
        AgentOptions agentOptions,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<ContainerMetric> containers =
                await dockerMetricsCollector.GetMetricsAsync(
                    cancellationToken);

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
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Docker metric collection failed on node {NodeName}.",
                agentOptions.NodeName);
        }
    }
}