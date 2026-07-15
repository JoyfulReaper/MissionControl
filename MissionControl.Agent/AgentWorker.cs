using Microsoft.Extensions.Options;
using MissionControl.Agent.Docker;
using MissionControl.Agent.Models;
using MissionControl.Agent.Protocols;

namespace MissionControl.Agent;

internal sealed class AgentWorker(
    ILogger<AgentWorker> logger,
    IDockerMetricsCollector dockerMetricsCollector,
    ProtocolProbeRunner protocolProbeRunner,
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

            if (agentOptions.Probes.Length > 0)
            {
                await RunProtocolProbesAsync(
                    agentOptions,
                    stoppingToken);
            }

            // Protocol probes will run here later,
            // regardless of whether Docker metrics are enabled.
        }
        while (await timer.WaitForNextTickAsync(
                   stoppingToken));
    }

    private async Task RunProtocolProbesAsync(
    AgentOptions agentOptions,
    CancellationToken cancellationToken)
    {
        IReadOnlyList<ProtocolProbeResult> results =
            await protocolProbeRunner.RunAsync(
                agentOptions.Probes,
                cancellationToken);

        foreach (var result in results)
        {
            if (result.Succeeded)
            {
                logger.LogInformation(
                    "{Service} probe to {Endpoint} succeeded in {DurationMilliseconds} ms.",
                    result.Service,
                    result.Endpoint,
                    result.DurationMilliseconds);
            }
            else
            {
                logger.LogWarning(
                    "{Service} probe to {Endpoint} failed after {DurationMilliseconds} ms: {Error}",
                    result.Service,
                    result.Endpoint,
                    result.DurationMilliseconds,
                    result.Error);
            }
        }
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