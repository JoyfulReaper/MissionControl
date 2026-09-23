#!/usr/bin/env bash
set -euo pipefail

required=(
  "MissionControl.Dashboard/MobileApi/MobileApiEndpointRouteBuilderExtensions.cs"
  "MissionControl.Tests/MobileApiEndpointTests.cs"
  "MissionControl.Client/Infrastructure"
  "MissionControl.Dashboard/MobileApi"
)

for path in "${required[@]}"; do
  if [[ ! -e "$path" ]]; then
    echo "Missing expected path: $path" >&2
    echo "Run this from the MissionControl repository root." >&2
    exit 1
  fi
done

cat > MissionControl.Client/Infrastructure/HostNodeSnapshot.cs <<'EOF'
using MissionControl.Contracts.Agent;

namespace MissionControl.Client.Infrastructure;

public sealed record HostNodeSnapshot(
    string NodeId,
    string DisplayName,
    PublicNodeSnapshot? AgentSnapshot,
    string? AgentError,
    bool BandwidthConfigured,
    BandwidthUsageSnapshot? Bandwidth,
    string? BandwidthError);
EOF

cat > MissionControl.Client/Infrastructure/IHostFleetClient.cs <<'EOF'
namespace MissionControl.Client.Infrastructure;

public interface IHostFleetClient
{
    Task<IReadOnlyList<HostNodeSnapshot>> GetAsync(
        CancellationToken cancellationToken = default);
}
EOF

cat > MissionControl.Client/Infrastructure/HostFleetClient.cs <<'EOF'
using System.Net.Http.Json;

namespace MissionControl.Client.Infrastructure;

public sealed class HostFleetClient(
    HttpClient client) : IHostFleetClient
{
    public async Task<IReadOnlyList<HostNodeSnapshot>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response =
            await client.GetAsync(
                "api/mobile/hosts",
                cancellationToken);

        response.EnsureSuccessStatusCode();

        HostNodeSnapshot[]? hosts =
            await response.Content
                .ReadFromJsonAsync<HostNodeSnapshot[]>(
                    cancellationToken);

        return hosts ??
            throw new InvalidOperationException(
                "The hosts API response was empty.");
    }
}
EOF

cat > MissionControl.Dashboard/MobileApi/HostsMobileApiEndpointRouteBuilderExtensions.cs <<'EOF'
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
        IGreenCloudBandwidthFleetClient bandwidthFleetClient,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        DisableResponseCaching(response);

        try
        {
            Task<IReadOnlyList<AgentNodeResult>> agentTask =
                agentFleetClient.GetSnapshotsAsync(
                    cancellationToken);

            Task<IReadOnlyList<GreenCloudBandwidthNodeResult>>
                bandwidthTask =
                    bandwidthFleetClient.GetAllAsync(
                        cancellationToken);

            await Task.WhenAll(
                agentTask,
                bandwidthTask);

            IReadOnlyList<AgentNodeResult> agentNodes =
                await agentTask;

            IReadOnlyList<GreenCloudBandwidthNodeResult>
                bandwidthNodes =
                    await bandwidthTask;

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
EOF

python3 <<'PY'
from pathlib import Path

def read(path):
    return Path(path).read_text(encoding="utf-8-sig")

def write(path, text):
    Path(path).write_text(text, encoding="utf-8")

def replace_once(text, old, new, label):
    if old not in text:
        raise SystemExit(f"Expected insertion point not found: {label}")
    return text.replace(old, new, 1)

path = "MissionControl.Dashboard/MobileApi/MobileApiEndpointRouteBuilderExtensions.cs"
text = read(path)
text = replace_once(
    text,
    """        endpoints.MapWorkPlanningMobileApiEndpoints();
        endpoints.MapBandwidthMobileApiEndpoint();

        return endpoints;""",
    """        endpoints.MapWorkPlanningMobileApiEndpoints();
        endpoints.MapBandwidthMobileApiEndpoint();
        endpoints.MapHostsMobileApiEndpoint();

        return endpoints;""",
    "mobile endpoint registration",
)
write(path, text)

path = "MissionControl.Tests/MobileApiEndpointTests.cs"
text = read(path)

text = replace_once(
    text,
    "using DashboardApp::MissionControl.Dashboard.MobileApi;\n",
    """using DashboardApp::MissionControl.Dashboard.MobileApi;
using AgentNodeResult =
    DashboardApp::MissionControl.Dashboard.Agents.AgentNodeResult;
using IAgentFleetClient =
    DashboardApp::MissionControl.Dashboard.Agents.IAgentFleetClient;
using GreenCloudBandwidthNodeResult =
    DashboardApp::MissionControl.Dashboard.GreenCloud.GreenCloudBandwidthNodeResult;
using IGreenCloudBandwidthFleetClient =
    DashboardApp::MissionControl.Dashboard.GreenCloud.IGreenCloudBandwidthFleetClient;
""",
    "dashboard alias usings",
)

text = replace_once(
    text,
    """using MissionControl.Contracts.Archive;
using MissionControl.Contracts.GitActivity;
""",
    """using MissionControl.Contracts.Agent;
using MissionControl.Contracts.Archive;
using MissionControl.Contracts.GitActivity;
""",
    "agent contract using",
)

new_tests = r"""    [Fact]
    public async Task HostsProxyReturnsFleetWithBandwidthAndDisablesCaching()
    {
        PublicNodeSnapshot agentSnapshot =
            CreateAgentSnapshot("clanker");

        BandwidthUsageSnapshot bandwidthSnapshot =
            CreateBandwidthSnapshot();

        var agentFleetClient =
            new RecordingAgentFleetClient(
                [
                    new AgentNodeResult(
                        "clanker",
                        "Clanker",
                        agentSnapshot,
                        Error: null)
                ]);

        var bandwidthFleetClient =
            new RecordingGreenCloudBandwidthFleetClient(
                [
                    new GreenCloudBandwidthNodeResult(
                        "clanker",
                        bandwidthSnapshot,
                        Error: null)
                ]);

        await using WebApplication app =
            CreateApplication(
                new FailingArchiveEventClient(
                    new HttpRequestException()),
                agentFleetClient: agentFleetClient,
                bandwidthFleetClient:
                    bandwidthFleetClient);

        await app.StartAsync();

        using HttpClient client =
            app.GetTestClient();

        using var request =
            CreateAuthorizedRequest(
                "/api/mobile/hosts");

        using HttpResponseMessage response =
            await client.SendAsync(request);

        HostNodeSnapshot[]? hosts =
            await response.Content
                .ReadFromJsonAsync<HostNodeSnapshot[]>();

        HostNodeSnapshot host =
            Assert.Single(hosts!);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.Equal(
            "clanker",
            host.NodeId);
        Assert.Equal(
            "Clanker",
            host.DisplayName);
        Assert.Equal(
            agentSnapshot,
            host.AgentSnapshot);
        Assert.Null(host.AgentError);
        Assert.True(host.BandwidthConfigured);
        Assert.Equal(
            bandwidthSnapshot,
            host.Bandwidth);
        Assert.Null(host.BandwidthError);
        Assert.True(agentFleetClient.WasCalled);
        Assert.True(
            bandwidthFleetClient.WasCalled);
        Assert.Contains(
            "no-store",
            response.Headers.CacheControl?.ToString());
        Assert.Contains(
            "no-cache",
            response.Headers.Pragma.ToString());
    }

    [Fact]
    public async Task HostsProxySanitizesPerNodeFailures()
    {
        const string secret =
            "private-internal-detail";

        var agentFleetClient =
            new RecordingAgentFleetClient(
                [
                    new AgentNodeResult(
                        "scopecreep",
                        "ScopeCreep",
                        Snapshot: null,
                        Error:
                            $"Connection refused at " +
                            $"http://10.99.0.9:5194/{secret}")
                ]);

        var bandwidthFleetClient =
            new RecordingGreenCloudBandwidthFleetClient(
                [
                    new GreenCloudBandwidthNodeResult(
                        "scopecreep",
                        Snapshot: null,
                        Error:
                            $"GreenCloud failure using " +
                            secret)
                ]);

        await using WebApplication app =
            CreateApplication(
                new FailingArchiveEventClient(
                    new HttpRequestException()),
                agentFleetClient: agentFleetClient,
                bandwidthFleetClient:
                    bandwidthFleetClient);

        await app.StartAsync();

        using HttpClient client =
            app.GetTestClient();

        using var request =
            CreateAuthorizedRequest(
                "/api/mobile/hosts");

        using HttpResponseMessage response =
            await client.SendAsync(request);

        string body =
            await response.Content.ReadAsStringAsync();

        HostNodeSnapshot[]? hosts =
            System.Text.Json.JsonSerializer
                .Deserialize<HostNodeSnapshot[]>(body);

        HostNodeSnapshot host =
            Assert.Single(hosts!);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.Equal(
            "Agent snapshot could not be retrieved.",
            host.AgentError);
        Assert.True(host.BandwidthConfigured);
        Assert.Equal(
            "GreenCloud bandwidth could not be retrieved.",
            host.BandwidthError);
        Assert.DoesNotContain(
            secret,
            body);
        Assert.DoesNotContain(
            "10.99.0.9",
            body);
        Assert.DoesNotContain(
            "5194",
            body);
    }

"""

text = replace_once(
    text,
    """    [Fact]
    public async Task BandwidthProxyReturnsSnapshotAndDisablesCaching()
""",
    new_tests + """    [Fact]
    public async Task BandwidthProxyReturnsSnapshotAndDisablesCaching()
""",
    "host endpoint tests",
)

text = replace_once(
    text,
    """    private static WebApplication CreateApplication(
        IArchiveEventClient archiveClient,
        IGitActivityClient? gitActivityClient = null,
        IBandwidthUsageClient? bandwidthClient = null)
""",
    """    private static WebApplication CreateApplication(
        IArchiveEventClient archiveClient,
        IGitActivityClient? gitActivityClient = null,
        IBandwidthUsageClient? bandwidthClient = null,
        IAgentFleetClient? agentFleetClient = null,
        IGreenCloudBandwidthFleetClient?
            bandwidthFleetClient = null)
""",
    "test app signature",
)

text = replace_once(
    text,
    """        builder.Services.AddSingleton(
            bandwidthClient ??
            new RecordingBandwidthUsageClient(
                CreateBandwidthSnapshot()));
        builder.Services.AddSingleton<IWorkPlanningClient>(
            new StubWorkPlanningClient());
""",
    """        builder.Services.AddSingleton(
            bandwidthClient ??
            new RecordingBandwidthUsageClient(
                CreateBandwidthSnapshot()));

        builder.Services.AddSingleton<IAgentFleetClient>(
            agentFleetClient ??
            new RecordingAgentFleetClient([]));

        builder.Services
            .AddSingleton<IGreenCloudBandwidthFleetClient>(
                bandwidthFleetClient ??
                new RecordingGreenCloudBandwidthFleetClient(
                    []));

        builder.Services.AddSingleton<IWorkPlanningClient>(
            new StubWorkPlanningClient());
""",
    "test fleet service registrations",
)

text = replace_once(
    text,
    """    private static BandwidthUsageSnapshot CreateBandwidthSnapshot()
    {
""",
    """    private static PublicNodeSnapshot CreateAgentSnapshot(
        string nodeId)
    {
        return new PublicNodeSnapshot(
            nodeId,
            DateTimeOffset.Parse(
                "2026-09-23T20:00:00Z"),
            5,
            Stale: false,
            Host: new PublicHostMetric(
                2,
                12.5,
                4_000_000_000,
                3_000_000_000),
            MissionControlPublishSucceeded: true,
            LastMissionControlPublishAttemptAt:
                DateTimeOffset.Parse(
                    "2026-09-23T20:00:00Z"),
            Protocols: [],
            Containers: [],
            DockerAvailable: true,
            DockerError: null)
        {
            NodeId = nodeId,
            HostCapturedAt =
                DateTimeOffset.Parse(
                    "2026-09-23T20:00:00Z")
        };
    }

    private static BandwidthUsageSnapshot CreateBandwidthSnapshot()
    {
""",
    "agent snapshot test helper",
)

fleet_stubs = r"""    private sealed class RecordingAgentFleetClient(
        IReadOnlyList<AgentNodeResult> nodes)
        : IAgentFleetClient
    {
        public bool WasCalled { get; private set; }

        public Task<IReadOnlyList<AgentNodeResult>>
            GetSnapshotsAsync(
                CancellationToken cancellationToken = default)
        {
            WasCalled = true;

            return Task.FromResult(nodes);
        }
    }

    private sealed class RecordingGreenCloudBandwidthFleetClient(
        IReadOnlyList<GreenCloudBandwidthNodeResult> nodes)
        : IGreenCloudBandwidthFleetClient
    {
        public bool WasCalled { get; private set; }

        public Task<IReadOnlyList<GreenCloudBandwidthNodeResult>>
            GetAllAsync(
                CancellationToken cancellationToken = default)
        {
            WasCalled = true;

            return Task.FromResult(nodes);
        }
    }

"""

text = replace_once(
    text,
    """    private sealed class StubWorkPlanningClient
        : IWorkPlanningClient
""",
    fleet_stubs + """    private sealed class StubWorkPlanningClient
        : IWorkPlanningClient
""",
    "fleet test stubs",
)

write(path, text)
PY

git diff --check
git status -sb

echo
echo "Patch applied. Next:"
echo "  dotnet test MissionControl.Tests/MissionControl.Tests.csproj"
echo "  git diff --stat"
