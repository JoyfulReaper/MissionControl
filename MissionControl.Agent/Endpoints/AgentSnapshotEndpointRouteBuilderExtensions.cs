using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using MissionControl.Agent.DependencyInjection;
using MissionControl.Agent.Storage;
using MissionControl.Contracts.Agent;

namespace MissionControl.Agent.Endpoints;

public static class AgentSnapshotEndpointRouteBuilderExtensions
{
    public static RouteHandlerBuilder MapAgentSnapshotEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        return endpoints
            .MapGet(
                "/api/snapshot",
                HandleGetSnapshotAsync)
            .WithName("GetAgentSnapshot")
            .WithTags("Agent Snapshot")
            .Produces<PublicNodeSnapshot>(
                StatusCodes.Status200OK)
            .Produces<ProblemDetails>(
                StatusCodes.Status503ServiceUnavailable)
            .RequireCors(
                AgentApiServiceCollectionExtensions
                    .CorsPolicyName)
            .RequireRateLimiting(
                AgentApiServiceCollectionExtensions
                    .RateLimitPolicyName);
    }

    private static async Task<IResult> HandleGetSnapshotAsync(
        INodeSnapshotStore snapshotStore,
        IOptions<AgentOptions> agentOptionsAccessor,
        IOptions<AgentApiOptions> apiOptionsAccessor,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        AgentOptions agentOptions =
            agentOptionsAccessor.Value;

        AgentApiOptions apiOptions =
            apiOptionsAccessor.Value;

        StoredNodeSnapshot? storedSnapshot =
            await snapshotStore.GetAsync(
                agentOptions.NodeName,
                cancellationToken);

        if (storedSnapshot is null)
        {
            response.Headers["Retry-After"] = "5";
            response.Headers["Cache-Control"] =
                "no-store";

            return Results.Problem(
                title:
                    "No node snapshot is currently available.",
                statusCode:
                    StatusCodes.Status503ServiceUnavailable);
        }

        PublicNodeSnapshot publicSnapshot =
            CreatePublicSnapshot(
                storedSnapshot,
                DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(
                    apiOptions.StaleAfterSeconds));

        response.Headers["Cache-Control"] =
            "public, max-age=15";

        return Results.Ok(publicSnapshot);
    }

    internal static PublicNodeSnapshot CreatePublicSnapshot(
        StoredNodeSnapshot storedSnapshot,
        DateTimeOffset now,
        TimeSpan staleAfter)
    {
        TimeSpan age =
            now - storedSnapshot.Snapshot.CapturedAt;

        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        PublicProtocolStatus[] protocols =
            storedSnapshot.Snapshot.Protocols
                .Select(protocol =>
                    new PublicProtocolStatus(
                        Service: protocol.Service,
                        Succeeded: protocol.Succeeded,
                        DurationMilliseconds:
                            protocol.DurationMilliseconds,
                        Endpoint:
                            PublicDiagnosticSanitizer
                                .SanitizeEndpoint(protocol.Endpoint),
                        Error:
                            PublicDiagnosticSanitizer
                                .SanitizeError(
                                    protocol.Error,
                                    protocol.Succeeded)))
                .ToArray();

        PublicContainerStatus[] containers =
            storedSnapshot.Snapshot.Containers
                .Select(container =>
                    new PublicContainerStatus(
                        Name: container.Name,
                        State: container.State,
                        MemoryUsageBytes:
                            container.MemoryUsageBytes,
                        MemoryLimitBytes:
                            container.MemoryLimitBytes,
                        MemoryPercent:
                            container.MemoryPercent,
                        CpuPercent:
                            container.CpuPercent,
                        RestartCount:
                            container.RestartCount,
                        Image:
                            string.IsNullOrWhiteSpace(container.Image)
                                ? null
                                : container.Image))
                .ToArray();

        return new PublicNodeSnapshot(
            Node:
                storedSnapshot.Snapshot.Node,
            CapturedAt:
                storedSnapshot.Snapshot.CapturedAt,
            AgeSeconds:
                (long)Math.Floor(age.TotalSeconds),
            Stale:
                age > staleAfter,
            Host:
                storedSnapshot.Snapshot.Host is null
                    ? null
                    : new PublicHostMetric(
                        LogicalProcessorCount:
                            storedSnapshot.Snapshot.Host
                                .LogicalProcessorCount,
                        CpuPercent:
                            storedSnapshot.Snapshot.Host.CpuPercent,
                        MemoryTotalBytes:
                            storedSnapshot.Snapshot.Host
                                .MemoryTotalBytes,
                        MemoryAvailableBytes:
                            storedSnapshot.Snapshot.Host
                                .MemoryAvailableBytes),
            MissionControlPublishSucceeded:
                storedSnapshot.PublishSucceeded,
            LastMissionControlPublishAttemptAt:
                storedSnapshot.LastPublishAttemptAt,
            Protocols:
                protocols,
            Containers:
                containers,
            DockerAvailable:
                storedSnapshot.Snapshot.DockerAvailable,
            DockerError:
                storedSnapshot.Snapshot.DockerError);
    }
}
