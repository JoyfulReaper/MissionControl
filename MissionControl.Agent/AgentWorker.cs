using JoyfulReaperLib.MissionControl;
using Microsoft.Extensions.Options;
using MissionControl.Agent.Docker;
using MissionControl.Agent.Host;
using MissionControl.Agent.Models;
using MissionControl.Agent.Protocols;
using MissionControl.Agent.Publishing;
using MissionControl.Agent.Storage;

namespace MissionControl.Agent;

internal sealed class AgentWorker(
    ILogger<AgentWorker> logger,
    IDockerMetricsCollector dockerMetricsCollector,
    IHostMetricsCollector hostMetricsCollector,
    IMissionControlClient missionControlClient,
    ProtocolProbeRunner protocolProbeRunner,
    INodeSnapshotStore snapshotStore,
    SnapshotPublicationGate publicationGate,
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
                await ExecuteIterationAsync(
                    agentOptions,
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

    internal async Task ExecuteIterationAsync(
        AgentOptions agentOptions,
        CancellationToken cancellationToken)
    {
        Task<DockerMetricsCollectionResult> dockerTask =
            agentOptions.DockerEnabled
                ? CollectDockerMetricsAsync(cancellationToken)
                : Task.FromResult(
                    new DockerMetricsCollectionResult(
                        Succeeded: false,
                        Containers: [],
                        Error: "Docker metric collection is disabled."));

        Task<IReadOnlyList<ProtocolProbeResult>> protocolTask =
            agentOptions.Probes.Length > 0
                ? RunProtocolProbesAsync(
                    agentOptions,
                    cancellationToken)
                : Task.FromResult<IReadOnlyList<ProtocolProbeResult>>([]);

        Task<HostMetric?> hostTask =
            CollectHostMetricsAsync(cancellationToken);

        await Task.WhenAll(
            dockerTask,
            protocolTask,
            hostTask);

        DockerMetricsCollectionResult docker =
            await dockerTask;

        var snapshot = new NodeSnapshotEvent(
            Node: agentOptions.NodeName,
            CapturedAt: DateTimeOffset.UtcNow,
            Host: await hostTask,
            Protocols: await protocolTask,
            Containers: docker.Containers,
            DockerAvailable: docker.Succeeded,
            DockerError: docker.Error);

        await snapshotStore.SaveAsync(
            snapshot,
            cancellationToken);

        DateTimeOffset publicationTime =
            DateTimeOffset.UtcNow;

        if (!publicationGate.IsDue(
                snapshot,
                publicationTime))
        {
            logger.LogDebug(
                "Node snapshot for {NodeName} was saved but publication was suppressed because no operational state changed.",
                snapshot.Node);

            return;
        }

        bool published =
            await PublishSnapshotAsync(
                snapshot,
                cancellationToken);

        await snapshotStore.RecordPublishResultAsync(
            node: snapshot.Node,
            capturedAt: snapshot.CapturedAt,
            succeeded: published,
            attemptedAt: publicationTime,
            cancellationToken: cancellationToken);

        if (published)
        {
            publicationGate.MarkPublished(
                snapshot,
                publicationTime);
        }
    }

    private async Task<bool> PublishSnapshotAsync(
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

            if (!published)
            {
                logger.LogWarning(
                    "Mission Control rejected or failed to publish the node snapshot for {NodeName}.",
                    snapshot.Node);
            }

            return published;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to publish node snapshot for {NodeName}.",
                snapshot.Node);

            return false;
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

    private async Task<DockerMetricsCollectionResult>
        CollectDockerMetricsAsync(
            CancellationToken cancellationToken)
    {
        try
        {
            return await dockerMetricsCollector.GetMetricsAsync(
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            logger.LogWarning(
                exception,
                "Docker metric collection timed out.");

            return new DockerMetricsCollectionResult(
                Succeeded: false,
                Containers: [],
                Error: "Docker metric collection timed out.");
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Docker metric collection is unavailable.");

            return new DockerMetricsCollectionResult(
                Succeeded: false,
                Containers: [],
                Error: "Docker metric collection is unavailable.");
        }
    }

    private async Task<HostMetric?> CollectHostMetricsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return await hostMetricsCollector.GetMetricsAsync(
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Host metric collection failed.");

            return null;
        }
    }
}
