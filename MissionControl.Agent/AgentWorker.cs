using JoyfulReaperLib.MissionControl;
using Microsoft.Extensions.Options;
using MissionControl.Agent.Docker;
using MissionControl.Agent.Models;
using MissionControl.Agent.Protocols;

namespace MissionControl.Agent;

internal sealed class AgentWorker(
    ILogger<AgentWorker> logger,
    IDockerMetricsCollector dockerMetricsCollector,
    IMissionControlClient missionControlClient,
    ProtocolProbeRunner protocolProbeRunner,
    IOptions<AgentOptions> options) : BackgroundService
{
    private const string SnapshotEventType =
        "missioncontrol.agent.node.snapshot";

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
            try
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

                await PublishSnapshotAsync(
                    snapshot,
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Node snapshot collection failed for {NodeName}.",
                    agentOptions.NodeName);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PublishSnapshotAsync(
        NodeSnapshotEvent snapshot,
        CancellationToken cancellationToken)
    {
        try
        {
            bool published =
                await missionControlClient.TryPublishAsync(
                    eventType: SnapshotEventType,
                    payload: snapshot,
                    occurredAt: snapshot.CapturedAt,
                    correlationId: null,
                    cancellationToken: cancellationToken);

            if (published)
            {
                logger.LogDebug(
                    "Published node snapshot for {NodeName} with " +
                    "{ContainerCount} containers and {ProtocolCount} protocol results.",
                    snapshot.Node,
                    snapshot.Containers.Count,
                    snapshot.Protocols.Count);
            }
            else
            {
                logger.LogWarning(
                    "Mission Control rejected or failed to publish the node snapshot for {NodeName}.",
                    snapshot.Node);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The client is intended to be best-effort, but this protects
            // the agent from unexpected custom/client implementation errors.
            logger.LogWarning(
                ex,
                "Failed to publish node snapshot for {NodeName}.",
                snapshot.Node);
        }
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