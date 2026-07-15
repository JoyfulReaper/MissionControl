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
            Task<IReadOnlyList<ContainerMetric>> containerTask =
                agentOptions.DockerEnabled
                    ? CollectDockerMetricsAsync(stoppingToken)
                    : Task.FromResult<IReadOnlyList<ContainerMetric>>([]);

            Task<IReadOnlyList<ProtocolProbeResult>> protocolTask =
                agentOptions.Probes.Length > 0
                    ? RunProtocolProbesAsync(
                        agentOptions,
                        stoppingToken)
                    : Task.FromResult<IReadOnlyList<ProtocolProbeResult>>([]);

            await Task.WhenAll(containerTask, protocolTask);
            var snapshot = new NodeSnapshotEvent(
                Node: agentOptions.NodeName,
                CapturedAt: DateTimeOffset.UtcNow,
                Protocols: await protocolTask,
                Containers: await containerTask);
        }
        while (await timer.WaitForNextTickAsync(
                   stoppingToken));
    }

    private async Task<IReadOnlyList<ProtocolProbeResult>> RunProtocolProbesAsync(
        AgentOptions agentOptions,
        CancellationToken cancellationToken)
    {
        return
            await protocolProbeRunner.RunAsync(
                agentOptions.Probes,
                cancellationToken);
    }

    private async Task<IReadOnlyList<ContainerMetric>>
        CollectDockerMetricsAsync(
            CancellationToken cancellationToken)
    {
        return await dockerMetricsCollector.GetMetricsAsync(
            cancellationToken);
    }
}