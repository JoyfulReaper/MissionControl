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
using MissionControl.Contracts.Archive;
using MissionControl.Contracts.GitActivity;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Xunit;

namespace MissionControl.Tests;

public sealed class MobileApiEndpointTests
{
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

    private static WebApplication CreateApplication(
        IArchiveEventClient archiveClient,
        IGitActivityClient? gitActivityClient = null,
        IBandwidthUsageClient? bandwidthClient = null)
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
        builder.Services.AddSingleton<IWorkPlanningClient>(
            new StubWorkPlanningClient());

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

    private sealed class StubWorkPlanningClient
        : IWorkPlanningClient
    {
        public Task<DailyWorkPick?> GetDailyPickAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<DailyWorkPick?>(null);
        }

        public Task<IReadOnlyList<WorkPlanningWorkItem>>
            GetWorkItemsAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<WorkPlanningWorkItem>>(
                []);
        }

        public Task<WorkPlanningTodo> CreateTodoAsync(
            int workItemId,
            CreateWorkPlanningTodoRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException<WorkPlanningTodo>(
                new InvalidOperationException(
                    "Todo creation is not configured for this test."));
        }
    }
}
