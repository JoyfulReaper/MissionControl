using MissionControl.Client.Infrastructure;
using MissionControl.Dashboard.Agents;
using MissionControl.Dashboard.GreenCloud;

namespace MissionControl.Dashboard.MobileApi;

public static class HostsMobileApiEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapHostsMobileApiEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints
            .MapGet(
                "/api/mobile/hosts",
                HandleGetHostsAsync)
            .WithName("GetMobileHosts")
            .WithTags(
                "Mobile API",
                "Infrastructure")
            .Produces<IReadOnlyList<HostNodeSnapshot>>(
                StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .RequireAuthorization(
                MobileApiAuthenticationDefaults.Policy);

        return endpoints;
    }

    private static async Task<IResult> HandleGetHostsAsync(
        IAgentFleetClient agentFleetClient,
        GreenCloudBandwidthFleetRefreshController bandwidthState,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        DisableResponseCaching(response);

        try
        {
            IReadOnlyList<AgentNodeResult> agentNodes =
                await agentFleetClient.GetSnapshotsAsync(
                    cancellationToken);

            IReadOnlyList<GreenCloudBandwidthNodeResult>
                bandwidthNodes =
                    bandwidthState.CurrentNodes;

            Dictionary<string, GreenCloudBandwidthNodeResult>
                bandwidthByNode =
                    bandwidthNodes.ToDictionary(
                        node => node.NodeId,
                        StringComparer.OrdinalIgnoreCase);

            HostNodeSnapshot[] hosts =
                agentNodes
                    .Select(
                        node =>
                        {
                            bool bandwidthConfigured =
                                bandwidthByNode.TryGetValue(
                                    node.NodeId,
                                    out GreenCloudBandwidthNodeResult?
                                        bandwidth);

                            return new HostNodeSnapshot(
                                node.NodeId,
                                node.DisplayName,
                                node.Snapshot,
                                SanitizeAgentError(node.Error),
                                bandwidthConfigured,
                                bandwidth?.Snapshot,
                                SanitizeBandwidthError(
                                    bandwidth?.Error));
                        })
                    .ToArray();

            return Results.Ok(hosts);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is
                HttpRequestException or
                TaskCanceledException or
                InvalidOperationException or
                System.Text.Json.JsonException)
        {
            return Results.Problem(
                title: "Host fleet unavailable.",
                detail:
                    "Mission Control host telemetry could not be retrieved.",
                statusCode:
                    StatusCodes.Status502BadGateway);
        }
    }

    private static string? SanitizeAgentError(
        string? error)
    {
        return string.IsNullOrWhiteSpace(error)
            ? null
            : "Agent snapshot could not be retrieved.";
    }

    private static string? SanitizeBandwidthError(
        string? error)
    {
        return string.IsNullOrWhiteSpace(error)
            ? null
            : "GreenCloud bandwidth could not be retrieved.";
    }

    private static void DisableResponseCaching(
        HttpResponse response)
    {
        response.Headers["Cache-Control"] =
            "no-store";
        response.Headers["Pragma"] =
            "no-cache";
    }
}
