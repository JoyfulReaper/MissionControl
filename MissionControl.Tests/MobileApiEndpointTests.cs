extern alias DashboardApp;
using DashboardApp::MissionControl.Dashboard.MobileApi;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MissionControl.Client.Archive;
using MissionControl.Client.GitActivity;
using MissionControl.Client.Infrastructure;
using MissionControl.Client.WorkPlanning;
using MissionControl.Contracts.Agent;
using MissionControl.Contracts.Archive;
using MissionControl.Contracts.GitActivity;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Xunit;
using AgentNodeResult =
    DashboardApp::MissionControl.Dashboard.Agents.AgentNodeResult;
using BandwidthNodeResult =
    DashboardApp::MissionControl.Dashboard.Bandwidth.BandwidthNodeResult;
using GreenCloudBandwidthFleetRefreshController =
    DashboardApp::MissionControl.Dashboard.GreenCloud.GreenCloudBandwidthFleetRefreshController;
using GreenCloudOptions =
    DashboardApp::MissionControl.Dashboard.GreenCloud.GreenCloudOptions;
using IAgentFleetClient =
    DashboardApp::MissionControl.Dashboard.Agents.IAgentFleetClient;
using IGreenCloudBandwidthFleetClient =
    DashboardApp::MissionControl.Dashboard.GreenCloud.IGreenCloudBandwidthFleetClient;

namespace MissionControl.Tests;

public sealed class MobileApiEndpointTests
{
    [Fact]
    public async Task HostsApiIncludesDiskMetricsWithoutAdditionalRequests()
    {
        var disk = new PublicDiskUsage("/data", 1000, 600, 400, 350, 60, "ext4");
        PublicNodeSnapshot snapshot = CreateAgentSnapshot("clanker");
        snapshot = snapshot with { Host = snapshot.Host! with { Disks = [disk], DiskError = "Disk usage could not be collected." } };
        var agent = new RecordingAgentFleetClient([new("clanker", "Clanker", snapshot, null)]);
        var bandwidth = new RecordingGreenCloudBandwidthFleetClient([]);
        await using WebApplication app = CreateApplication(new FailingArchiveEventClient(new HttpRequestException()),
            agentFleetClient: agent, bandwidthFleetClient: bandwidth);
        await app.StartAsync();
        using HttpClient client = app.GetTestClient();
        using var request = CreateAuthorizedRequest("/api/mobile/hosts");
        using HttpResponseMessage response = await client.SendAsync(request);
        HostNodeSnapshot host = Assert.Single((await response.Content.ReadFromJsonAsync<HostNodeSnapshot[]>())!);
        Assert.Equal(disk, Assert.Single(host.AgentSnapshot!.Host!.Disks!));
        Assert.Equal(snapshot.Host.DiskError, host.AgentSnapshot.Host.DiskError);
        Assert.Equal(1, bandwidth.CallCount);
    }

    [Theory]
    [InlineData("daily-pick", false)]
    [InlineData("random-pick", false)]
    [InlineData("work-items", false)]
    [InlineData("work-items/1/todos", true)]
    public async Task DisabledWorkRoutesRemainAuthenticatedAndNeverCallUpstream(string path, bool post)
    {
        await using WebApplication app = CreateApplication(new FailingArchiveEventClient(new HttpRequestException()), workEnabled: false);
        await app.StartAsync();
        using HttpClient client = app.GetTestClient();
        using var request = CreateAuthorizedRequest("/api/mobile/work-planning/" + path);
        if (post)
        {
            request.Method = HttpMethod.Post;
            request.Content = JsonContent.Create(new CreateWorkPlanningTodoRequest("Test"));
        }
        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("work_planning_disabled", body);
        Assert.Contains("disabled", body);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal(0, ((StubWorkPlanningClient)app.Services.GetRequiredService<IWorkPlanningClient>()).Calls);
        using var unauthorized = new HttpRequestMessage(post ? HttpMethod.Post : HttpMethod.Get, "/api/mobile/work-planning/" + path);
        if (post) unauthorized.Content = JsonContent.Create(new CreateWorkPlanningTodoRequest("Test"));
        using HttpResponseMessage denied = await client.SendAsync(unauthorized);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkStatusReportsFeatureWithoutCallingUpstream(bool enabled)
    {
        await using WebApplication app = CreateApplication(new FailingArchiveEventClient(new HttpRequestException()), workEnabled: enabled);
        await app.StartAsync();
        using HttpClient client = app.GetTestClient();
        using var request = CreateAuthorizedRequest("/api/mobile/work-planning/status");
        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(enabled, (await response.Content.ReadFromJsonAsync<WorkPlanningAvailability>())!.Enabled);
        Assert.Equal(0, ((StubWorkPlanningClient)app.Services.GetRequiredService<IWorkPlanningClient>()).Calls);
        using HttpResponseMessage denied = await client.GetAsync("/api/mobile/work-planning/status");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        if (enabled)
        {
            using var work = CreateAuthorizedRequest("/api/mobile/work-planning/work-items");
            using HttpResponseMessage result = await client.SendAsync(work);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal(1, ((StubWorkPlanningClient)app.Services.GetRequiredService<IWorkPlanningClient>()).Calls);
            using var failing = CreateAuthorizedRequest("/api/mobile/work-planning/work-items/1/todos");
            failing.Method = HttpMethod.Post;
            failing.Content = JsonContent.Create(new CreateWorkPlanningTodoRequest("Test"));
            using HttpResponseMessage unavailable = await client.SendAsync(failing);
            Assert.Equal(HttpStatusCode.BadGateway, unavailable.StatusCode);
            Assert.DoesNotContain("work_planning_disabled", await unavailable.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task HostsApiRetainsSnapshotsAndProjectsConfiguredFreshness()
    {
        PublicNodeSnapshot snapshot = CreateAgentSnapshot("clanker");
        var time = new HostTestTimeProvider(snapshot.CapturedAt);
        var agent = new RecordingAgentFleetClient([new("clanker", "Clanker", snapshot, null)]);
        var bandwidth = new RecordingGreenCloudBandwidthFleetClient([]);
        await using WebApplication app = CreateApplication(new FailingArchiveEventClient(new HttpRequestException()),
            agentFleetClient: agent, bandwidthFleetClient: bandwidth, timeProvider: time, staleAfterSeconds: 60);
        await app.StartAsync();
        using HttpClient client = app.GetTestClient();

        async Task<HostNodeSnapshot> ReadAsync()
        {
            using var request = CreateAuthorizedRequest("/api/mobile/hosts");
            using HttpResponseMessage response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return Assert.Single((await response.Content.ReadFromJsonAsync<HostNodeSnapshot[]>())!);
        }

        Assert.False((await ReadAsync()).AgentSnapshot!.Stale);
        agent.Nodes = [new("clanker", "Clanker", null, "private failure")];
        time.Now = snapshot.CapturedAt.AddSeconds(30);
        HostNodeSnapshot fresh = await ReadAsync();
        Assert.False(fresh.AgentSnapshot!.Stale);
        Assert.Equal(30, fresh.AgentSnapshot.AgeSeconds);
        Assert.Equal("Agent snapshot could not be retrieved.", fresh.AgentError);
        time.Now = snapshot.CapturedAt.AddSeconds(61);
        HostNodeSnapshot stale = await ReadAsync();
        Assert.True(stale.AgentSnapshot!.Stale);
        Assert.Equal(61, stale.AgentSnapshot.AgeSeconds);
        Assert.Equal(snapshot.CapturedAt, stale.AgentSnapshot.CapturedAt);
        Assert.Equal(60, stale.SnapshotStaleAfterSeconds);
        Assert.Equal(1, bandwidth.CallCount);
        agent.Nodes = [new("clanker", "Clanker", snapshot with { CapturedAt = time.Now }, null)];
        HostNodeSnapshot recovered = await ReadAsync();
        Assert.False(recovered.AgentSnapshot!.Stale);
        Assert.Equal(0, recovered.AgentSnapshot.AgeSeconds);
        Assert.Null(recovered.AgentError);
    }

    [Fact]
    public async Task ArchiveProxyDoesNotExposeInternalArchiveFailureDetails()
    {
        await using WebApplication app = CreateApplication(
            new FailingArchiveEventClient(
                new HttpRequestException(
                    "Connection refused at http://archive.internal:5191")));

        await app.StartAsync();

        using HttpClient client =
            app.GetTestClient();

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                "/api/events/statistics");

        request.Headers.Authorization =
            new("Test", "accepted");

        using HttpResponseMessage response =
            await client.SendAsync(request);

        string body =
            await response.Content.ReadAsStringAsync();

        Assert.Equal(
            HttpStatusCode.BadGateway,
            response.StatusCode);
        Assert.Contains(
            "The Mission Control Archive could not be reached.",
            body);
        Assert.DoesNotContain(
            "archive.internal",
            body);
        Assert.DoesNotContain(
            "5191",
            body);
        Assert.DoesNotContain(
            "Connection refused",
            body);
    }

    [Fact]
    public async Task GitActivityProxyRejectsAnonymousAndInvalidBearerRequests()
    {
        var gitActivityClient =
            new RecordingGitActivityClient([]);
        await using WebApplication app = CreateApplication(
            new FailingArchiveEventClient(
                new HttpRequestException()),
            gitActivityClient);
        await app.StartAsync();
        using HttpClient client = app.GetTestClient();

        using HttpResponseMessage anonymous =
            await client.GetAsync("/api/mobile/git-activity");
        using var invalidRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/mobile/git-activity");
        invalidRequest.Headers.Authorization =
            new("Test", "rejected");
        using HttpResponseMessage invalid =
            await client.SendAsync(invalidRequest);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        Assert.Null(gitActivityClient.LastLimit);
    }

    [Fact]
    public async Task GitActivityProxyReturnsItemsClampsLimitAndDisablesCaching()
    {
        GitActivityItem item = new(
            "JoyfulReaper/MissionControl",
            "dev",
            "0123456789abcdef",
            "Add shared Git Activity page",
            "Kyle Givler",
            "JoyfulReaper",
            DateTimeOffset.Parse("2026-07-20T18:00:00Z"),
            "https://example.test/commit");
        var gitActivityClient =
            new RecordingGitActivityClient([item]);
        await using WebApplication app = CreateApplication(
            new FailingArchiveEventClient(
                new HttpRequestException()),
            gitActivityClient);
        await app.StartAsync();
        using HttpClient client = app.GetTestClient();
        using var request = CreateAuthorizedRequest(
            "/api/mobile/git-activity?limit=500");

        using HttpResponseMessage response =
            await client.SendAsync(request);
        GitActivityItem[]? activity =
            await response.Content
                .ReadFromJsonAsync<GitActivityItem[]>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(50, gitActivityClient.LastLimit);
        Assert.Equal(item, Assert.Single(activity!));
        Assert.Contains(
            "no-store",
            response.Headers.CacheControl?.ToString());
        Assert.Contains(
            "no-cache",
            response.Headers.Pragma.ToString());
    }

    [Fact]
    public async Task GitActivityProxySanitizesDownstreamFailures()
    {
        const string secret =
            "private-api-key-value";
        var gitActivityClient =
            new RecordingGitActivityClient(
                new HttpRequestException(
                    $"Connection refused using {secret} at http://gitactivity:8080"));
        await using WebApplication app = CreateApplication(
            new FailingArchiveEventClient(
                new HttpRequestException()),
            gitActivityClient);
        await app.StartAsync();
        using HttpClient client = app.GetTestClient();
        using var request = CreateAuthorizedRequest(
            "/api/mobile/git-activity");

        using HttpResponseMessage response =
            await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("Git Activity could not be reached.", body);
        Assert.DoesNotContain(secret, body);
        Assert.DoesNotContain("gitactivity:8080", body);
    }

    [Fact]
    public async Task HostsProxyReturnsCachedBandwidthAndDisablesCaching()
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
                    new BandwidthNodeResult(
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
        Assert.NotNull(host.AgentSnapshot);

        Assert.Equal(
            agentSnapshot.Node,
            host.AgentSnapshot.Node);

        Assert.Equal(
            agentSnapshot.NodeId,
            host.AgentSnapshot.NodeId);

        Assert.Equal(
            agentSnapshot.CapturedAt,
            host.AgentSnapshot.CapturedAt);

        Assert.Equal(
            agentSnapshot.AgeSeconds,
            host.AgentSnapshot.AgeSeconds);

        Assert.Equal(
            agentSnapshot.Stale,
            host.AgentSnapshot.Stale);

        Assert.Equal(
            agentSnapshot.Host,
            host.AgentSnapshot.Host);

        Assert.Equal(
            agentSnapshot.MissionControlPublishSucceeded,
            host.AgentSnapshot.MissionControlPublishSucceeded);

        Assert.Equal(
            agentSnapshot.LastMissionControlPublishAttemptAt,
            host.AgentSnapshot.LastMissionControlPublishAttemptAt);

        Assert.Empty(host.AgentSnapshot.Protocols);
        Assert.Empty(host.AgentSnapshot.Containers);

        Assert.Equal(
            agentSnapshot.DockerAvailable,
            host.AgentSnapshot.DockerAvailable);

        Assert.Equal(
            agentSnapshot.DockerError,
            host.AgentSnapshot.DockerError);

        Assert.Equal(
            agentSnapshot.HostCapturedAt,
            host.AgentSnapshot.HostCapturedAt);
        Assert.Null(host.AgentError);
        Assert.True(host.BandwidthConfigured);
        Assert.Equal(
            bandwidthSnapshot,
            host.Bandwidth);
        Assert.Null(host.BandwidthError);
        Assert.True(agentFleetClient.WasCalled);
        Assert.Equal(
            1,
            bandwidthFleetClient.CallCount);
        Assert.Contains(
            "no-store",
            response.Headers.CacheControl?.ToString());
        Assert.Contains(
            "no-cache",
            response.Headers.Pragma.ToString());
    }

    [Fact]
    public async Task HostsProxyReturnsAgentTelemetryDuringSlowBandwidthRefresh()
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

        var bandwidthClient =
            new BlockingAfterSuccessGreenCloudFleetClient(
                [
                    new BandwidthNodeResult(
                        "clanker",
                        bandwidthSnapshot,
                        Error: null)
                ]);

        var bandwidthState =
            new GreenCloudBandwidthFleetRefreshController(
                bandwidthClient,
                Options.Create(new GreenCloudOptions()));

        await bandwidthState.RefreshAsync(CancellationToken.None);

        Task<bool> slowRefresh =
            bandwidthState.RefreshAsync(CancellationToken.None);

        await bandwidthClient.SecondRequestStarted;

        try
        {
            await using WebApplication app =
                CreateApplication(
                    new FailingArchiveEventClient(
                        new HttpRequestException()),
                    agentFleetClient: agentFleetClient,
                    bandwidthState: bandwidthState);

            await app.StartAsync();

            using HttpClient client =
                app.GetTestClient();

            using var request =
                CreateAuthorizedRequest(
                    "/api/mobile/hosts");

            using HttpResponseMessage response =
                await client
                    .SendAsync(request)
                    .WaitAsync(TimeSpan.FromSeconds(2));

            HostNodeSnapshot[]? hosts =
                await response.Content
                    .ReadFromJsonAsync<HostNodeSnapshot[]>();

            HostNodeSnapshot host =
                Assert.Single(hosts!);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(host.AgentSnapshot);
            Assert.Equal(bandwidthSnapshot, host.Bandwidth);
            Assert.Equal(2, bandwidthClient.CallCount);
        }
        finally
        {
            bandwidthClient.ReleaseSecondRequest();
            await slowRefresh;
        }
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
                    new BandwidthNodeResult(
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
            .Deserialize<HostNodeSnapshot[]>(
                body,
                new System.Text.Json.JsonSerializerOptions(
                    System.Text.Json.JsonSerializerDefaults.Web));

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
            "Provider bandwidth could not be retrieved.",
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

    [Fact]
    public async Task BandwidthProxyReturnsSnapshotAndDisablesCaching()
    {
        BandwidthUsageSnapshot snapshot = CreateBandwidthSnapshot();
        var bandwidthClient =
            new RecordingBandwidthUsageClient(snapshot);
        await using WebApplication app = CreateApplication(
            new FailingArchiveEventClient(
                new HttpRequestException()),
            bandwidthClient: bandwidthClient);
        await app.StartAsync();
        using HttpClient client = app.GetTestClient();
        using var request = CreateAuthorizedRequest(
            "/api/mobile/bandwidth");

        using HttpResponseMessage response =
            await client.SendAsync(request);
        BandwidthUsageSnapshot? actual =
            await response.Content
                .ReadFromJsonAsync<BandwidthUsageSnapshot>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(snapshot, actual);
        Assert.True(bandwidthClient.WasCalled);
        Assert.Contains(
            "no-store",
            response.Headers.CacheControl?.ToString());
        Assert.Contains(
            "no-cache",
            response.Headers.Pragma.ToString());
    }

    [Theory]
    [InlineData(true, 1, HttpStatusCode.OK)]
    [InlineData(false, 1, HttpStatusCode.BadGateway)]
    [InlineData(true, 0, HttpStatusCode.BadGateway)]
    [InlineData(true, 2, HttpStatusCode.BadGateway)]
    public async Task BandwidthCompatibilityRouteRequiresOneEnabledCanonicalMapping(
        bool enabled, int serverCount, HttpStatusCode expectedStatus)
    {
        using var handler = new GreenCloudBandwidthClientTests.RecordingHandler();
        using var providerHttp = new HttpClient(handler) { BaseAddress = new Uri("https://greencloud.example/") };
        var options = new GreenCloudOptions
        {
            Enabled = enabled,
            ApiToken = "test-only-token",
            Servers = Enumerable.Range(0, serverCount).Select(index =>
                new DashboardApp::MissionControl.Dashboard.GreenCloud.GreenCloudServerOptions
                {
                    NodeId = $"node-{index}",
                    ServerId = $"provider-{index}"
                }).ToList()
        };
        var bandwidthClient = new DashboardApp::MissionControl.Dashboard.GreenCloud.GreenCloudBandwidthClient(
            providerHttp, Options.Create(options), new(), TimeProvider.System);
        await using WebApplication app = CreateApplication(
            new FailingArchiveEventClient(new HttpRequestException()), bandwidthClient: bandwidthClient);
        await app.StartAsync();
        using HttpClient client = app.GetTestClient();
        using HttpResponseMessage unauthorized = await client.GetAsync("/api/mobile/bandwidth");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Empty(handler.Requests);

        using var request = CreateAuthorizedRequest("/api/mobile/bandwidth");
        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        if (expectedStatus == HttpStatusCode.OK)
        {
            BandwidthUsageSnapshot snapshot = (await response.Content.ReadFromJsonAsync<BandwidthUsageSnapshot>())!;
            Assert.Equal("/api/server/provider-0", snapshot.ServerName);
            Assert.Single(handler.Requests);
        }
        else
        {
            Assert.Empty(handler.Requests);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Bandwidth usage could not be retrieved.", body);
            Assert.DoesNotContain("test-only-token", body);
            Assert.DoesNotContain("greencloud.example", body);
        }
    }

    [Fact]
    public async Task BandwidthProxySanitizesDownstreamFailures()
    {
        const string secret =
            "private-greencloud-api-key";
        var bandwidthClient =
            new RecordingBandwidthUsageClient(
                new HttpRequestException(
                    $"Connection refused using {secret} at http://greencloud:8080"));
        await using WebApplication app = CreateApplication(
            new FailingArchiveEventClient(
                new HttpRequestException()),
            bandwidthClient: bandwidthClient);
        await app.StartAsync();
        using HttpClient client = app.GetTestClient();
        using var request = CreateAuthorizedRequest(
            "/api/mobile/bandwidth");

        using HttpResponseMessage response =
            await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("GreenCloud could not be reached.", body);
        Assert.DoesNotContain(secret, body);
        Assert.DoesNotContain("greencloud:8080", body);
    }

    [Fact]
    public async Task HostsCombinesProviderCachesWithoutRefreshingEitherProvider()
    {
        var servitroSnapshot = CreateBandwidthSnapshot() with
        {
            RxBytes = 5000,
            TxBytes = 1000,
            UsedBytes = 1000
        };
        var servitroClient = new RecordingGreenCloudBandwidthFleetClient(
            [new BandwidthNodeResult("wurstcase", servitroSnapshot, null)]);
        var servitroState = new DashboardApp::MissionControl.Dashboard.Bandwidth.BandwidthFleetRefreshController(
            servitroClient, ["wurstcase"], "Servitro");
        await servitroState.RefreshAsync(CancellationToken.None);
        var greenClient = new RecordingGreenCloudBandwidthFleetClient(
            [new BandwidthNodeResult("clanker", CreateBandwidthSnapshot(), null)]);
        await using WebApplication app = CreateApplication(
            new FailingArchiveEventClient(new HttpRequestException()),
            agentFleetClient: new RecordingAgentFleetClient(
                [new("clanker", "Clanker", CreateAgentSnapshot("clanker"), null),
                 new("WURSTCASE", "Wurstcase", CreateAgentSnapshot("wurstcase"), null)]),
            bandwidthFleetClient: greenClient,
            additionalBandwidthState: servitroState);
        await app.StartAsync();
        using HttpClient client = app.GetTestClient();
        using var request = CreateAuthorizedRequest("/api/mobile/hosts");
        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        HostNodeSnapshot[] hosts = (await response.Content.ReadFromJsonAsync<HostNodeSnapshot[]>())!;
        Assert.Equal(2, hosts.Length);
        var wurstcase = Assert.Single(hosts, host => host.NodeId == "WURSTCASE");
        Assert.Equal(servitroSnapshot, wurstcase.Bandwidth);
        Assert.Equal(1, servitroClient.CallCount);
        Assert.Equal(1, greenClient.CallCount);
    }

    private static WebApplication CreateApplication(
        IArchiveEventClient archiveClient,
        IGitActivityClient? gitActivityClient = null,
        IBandwidthUsageClient? bandwidthClient = null,
        IAgentFleetClient? agentFleetClient = null,
        IGreenCloudBandwidthFleetClient?
            bandwidthFleetClient = null,
        GreenCloudBandwidthFleetRefreshController?
            bandwidthState = null,
        DashboardApp::MissionControl.Dashboard.Bandwidth.BandwidthFleetRefreshController?
            additionalBandwidthState = null,
        TimeProvider? timeProvider = null,
        int staleAfterSeconds = 120,
        bool workEnabled = true)
    {
        WebApplicationBuilder builder =
            WebApplication.CreateBuilder();

        builder.WebHost.UseTestServer();

        builder.Services
            .AddAuthentication("Test")
            .AddScheme<
                AuthenticationSchemeOptions,
                TestAuthenticationHandler>(
                "Test",
                _ =>
                {
                });

        builder.Services
            .AddAuthorizationBuilder()
            .AddPolicy(
                MobileApiAuthenticationDefaults.Policy,
                policy =>
                {
                    policy.AddAuthenticationSchemes("Test");
                    policy.RequireAuthenticatedUser();
                })
            .SetFallbackPolicy(
                new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build());

        builder.Services.AddSingleton(archiveClient);
        builder.Services.AddSingleton(
            gitActivityClient ??
            new RecordingGitActivityClient([]));
        builder.Services.AddSingleton(
            bandwidthClient ??
            new RecordingBandwidthUsageClient(
                CreateBandwidthSnapshot()));

        builder.Services.AddSingleton<IAgentFleetClient>(
            agentFleetClient ??
            new RecordingAgentFleetClient([]));
        builder.Services.AddSingleton(Options.Create(new DashboardApp::MissionControl.Dashboard.Configuration.DashboardRefreshOptions
        {
            SnapshotStaleAfterSeconds = staleAfterSeconds
        }));
        builder.Services.AddSingleton(provider => new DashboardApp::MissionControl.Dashboard.Refresh.AgentFleetRefreshController(
            provider.GetRequiredService<IAgentFleetClient>(),
            timeProvider ?? new HostTestTimeProvider(DateTimeOffset.Parse("2026-09-23T20:00:05Z")),
            TimeSpan.FromSeconds(staleAfterSeconds)));

        if (bandwidthState is null)
        {
            IGreenCloudBandwidthFleetClient fleetClient =
                bandwidthFleetClient ??
                new RecordingGreenCloudBandwidthFleetClient([]);

            bandwidthState =
                new GreenCloudBandwidthFleetRefreshController(
                    fleetClient,
                    Options.Create(new GreenCloudOptions()));

            bandwidthState
                .RefreshAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        builder.Services.AddSingleton(new DashboardApp::MissionControl.Dashboard.Bandwidth.BandwidthFleetState(
            additionalBandwidthState is null ? [bandwidthState] : [bandwidthState, additionalBandwidthState]));

        builder.Services.AddSingleton<IWorkPlanningClient>(
            new StubWorkPlanningClient());
        builder.Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(
            new DashboardApp::MissionControl.Dashboard.WorkPlanning.WorkPlanningApiOptions { Enabled = workEnabled }));

        WebApplication app =
            builder.Build();

        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMobileApiEndpoints();

        return app;
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
        string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new("Test", "accepted");
        return request;
    }

    private static PublicNodeSnapshot CreateAgentSnapshot(
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
        return new BandwidthUsageSnapshot(
            "test-server",
            "running",
            1_000_000,
            200_000,
            100_000,
            300_000,
            700_000,
            30,
            70,
            DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
            18,
            13,
            16_666.67,
            53_846.15,
            516_666.71,
            51.67,
            1_024,
            512,
            DateTimeOffset.Parse("2026-08-19T16:00:00Z"));
    }

    private sealed class FailingArchiveEventClient(
        Exception exception)
        : IArchiveEventClient
    {
        public Task<IReadOnlyList<ArchiveEventSummaryItem>> GetRecentAsync(
            int limit = 50,
            string? source = null,
            string? eventType = null,
            ArchiveEventCursor? before = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<IReadOnlyList<ArchiveEventSummaryItem>>(
                exception);
        }

        public Task<ArchiveEventDetailsItem?> GetByIdAsync(
            Guid eventId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<ArchiveEventDetailsItem?>(
                exception);
        }

        public Task<ArchiveStatisticsItem> GetStatisticsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<ArchiveStatisticsItem>(
                exception);
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(
            options,
            logger,
            encoder)
    {
        protected override Task<AuthenticateResult>
            HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(
                    "Authorization",
                    out var authorization) ||
                !string.Equals(
                    authorization,
                    "Test accepted",
                    StringComparison.Ordinal))
            {
                return Task.FromResult(
                    AuthenticateResult.Fail(
                        "A valid test bearer is required."));
            }

            var identity =
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "Test Mobile Client")],
                    Scheme.Name);

            return Task.FromResult(
                AuthenticateResult.Success(
                    new AuthenticationTicket(
                        new ClaimsPrincipal(identity),
                        Scheme.Name)));
        }
    }

    private sealed class RecordingGitActivityClient
        : IGitActivityClient
    {
        private readonly IReadOnlyList<GitActivityItem>? items;
        private readonly Exception? exception;

        public RecordingGitActivityClient(
            IReadOnlyList<GitActivityItem> items)
        {
            this.items = items;
        }

        public RecordingGitActivityClient(Exception exception)
        {
            this.exception = exception;
        }

        public int? LastLimit { get; private set; }

        public Task<IReadOnlyList<GitActivityItem>> GetRecentAsync(
            int? limit = null,
            CancellationToken cancellationToken = default)
        {
            LastLimit = limit;

            return exception is null
                ? Task.FromResult(items!)
                : Task.FromException<IReadOnlyList<GitActivityItem>>(
                    exception);
        }
    }

    private sealed class RecordingBandwidthUsageClient
        : IBandwidthUsageClient
    {
        private readonly BandwidthUsageSnapshot? snapshot;
        private readonly Exception? exception;

        public RecordingBandwidthUsageClient(
            BandwidthUsageSnapshot snapshot)
        {
            this.snapshot = snapshot;
        }

        public RecordingBandwidthUsageClient(Exception exception)
        {
            this.exception = exception;
        }

        public bool WasCalled { get; private set; }

        public Task<BandwidthUsageSnapshot> GetAsync(
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;

            return exception is null
                ? Task.FromResult(snapshot!)
                : Task.FromException<BandwidthUsageSnapshot>(
                    exception);
        }
    }

    private sealed class HostTestTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecordingAgentFleetClient(
        IReadOnlyList<AgentNodeResult> nodes)
        : IAgentFleetClient
    {
        public bool WasCalled { get; private set; }
        public IReadOnlyList<AgentNodeResult> Nodes { get; set; } = nodes;

        public Task<IReadOnlyList<AgentNodeResult>>
            GetSnapshotsAsync(
                CancellationToken cancellationToken = default)
        {
            WasCalled = true;

            return Task.FromResult(Nodes);
        }
    }

    private sealed class RecordingGreenCloudBandwidthFleetClient(
        IReadOnlyList<BandwidthNodeResult> nodes)
        : IGreenCloudBandwidthFleetClient
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<BandwidthNodeResult>>
            GetAllAsync(
                CancellationToken cancellationToken = default)
        {
            CallCount++;

            return Task.FromResult(nodes);
        }
    }

    private sealed class BlockingAfterSuccessGreenCloudFleetClient(
        IReadOnlyList<BandwidthNodeResult> nodes)
        : IGreenCloudBandwidthFleetClient
    {
        private readonly TaskCompletionSource secondRequestStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource releaseSecondRequest =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SecondRequestStarted =>
            secondRequestStarted.Task;

        public int CallCount { get; private set; }

        public async Task<
            IReadOnlyList<BandwidthNodeResult>>
            GetAllAsync(
                CancellationToken cancellationToken = default)
        {
            CallCount++;

            if (CallCount == 1)
            {
                return nodes;
            }

            secondRequestStarted.TrySetResult();
            await releaseSecondRequest.Task.WaitAsync(
                cancellationToken);

            return nodes;
        }

        public void ReleaseSecondRequest()
        {
            releaseSecondRequest.TrySetResult();
        }
    }

    private sealed class StubWorkPlanningClient
        : IWorkPlanningClient
    {
        public int Calls { get; private set; }
        public Task<DailyWorkPick?> GetDailyPickAsync(
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<DailyWorkPick?>(null);
        }

        public Task<RandomWorkPick?> GetRandomPickAsync(
            bool favorPriority = false,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<RandomWorkPick?>(null);
        }

        public Task<IReadOnlyList<WorkPlanningWorkItem>>
            GetWorkItemsAsync(
                CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<WorkPlanningWorkItem>>(
                []);
        }

        public Task<WorkPlanningTodo> CreateTodoAsync(
            int workItemId,
            CreateWorkPlanningTodoRequest request,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromException<WorkPlanningTodo>(
                new InvalidOperationException(
                    "Todo creation is not configured for this test."));
        }
    }
}
