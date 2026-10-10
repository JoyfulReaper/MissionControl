extern alias DashboardApp;

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using DashboardApp::MissionControl.Dashboard.Bandwidth;
using DashboardApp::MissionControl.Dashboard.GreenCloud;
using DashboardApp::MissionControl.Dashboard.Refresh;
using DashboardApp::MissionControl.Dashboard.Servitro;
using Microsoft.Extensions.Options;
using MissionControl.Client.Infrastructure;
using Xunit;

namespace MissionControl.Tests;

public sealed class ServitroBandwidthTests
{
    private const string Payload = """
        {"data":{"name":"WurstCase","state":{"status":"running","network":{"primary":{
          "traffic":{"rx":5717010526,"tx":1085227514,"total":6802238040}}}},
          "network":{"primary":{"limit":"1000 GB"}},"currentMonthlyPeriod":{
          "start":"2026-10-06T00:00:00.000000Z","end":"2026-11-05T23:59:59.999999Z"}}}
        """;
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-10T00:00:00Z");

    [Fact]
    public async Task SuccessUsesTxOnlyForQuotaAndProjectionAndSendsHeaders()
    {
        using var http = CreateHttp((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://platform.servitro.com/api/server/server%2Fid?state=true", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
            Assert.Equal("application/json", Assert.Single(request.Headers.Accept).MediaType);
            return Task.FromResult(Response());
        });
        var client = CreateClient(http);
        BandwidthNodeResult node = Assert.Single(await client.GetAllAsync());
        Assert.Equal("custom-node", node.NodeId);
        Assert.True(node.Succeeded);
        BandwidthUsageSnapshot snapshot = Assert.IsType<BandwidthUsageSnapshot>(node.Snapshot);
        Assert.Equal("WurstCase", snapshot.ServerName);
        Assert.Equal("running", snapshot.Status);
        Assert.Equal(5717010526d, snapshot.RxBytes);
        Assert.Equal(1085227514d, snapshot.TxBytes);
        Assert.Equal(snapshot.TxBytes, snapshot.UsedBytes);
        Assert.Equal(1000d * 1024 * 1024 * 1024, snapshot.MonthlyLimitBytes);
        Assert.Equal(snapshot.MonthlyLimitBytes - snapshot.TxBytes, snapshot.RemainingBytes);
        Assert.Equal(snapshot.TxBytes / snapshot.MonthlyLimitBytes * 100, snapshot.UsedPercent);
        Assert.Equal(100 - snapshot.UsedPercent, snapshot.RemainingPercent, 10);
        Assert.Equal(DateTimeOffset.Parse("2026-10-06T00:00:00Z"), snapshot.PeriodStart);
        Assert.Equal(DateTimeOffset.Parse("2026-11-05T23:59:59.999999Z"), snapshot.PeriodEnd);
        Assert.Equal(4, snapshot.DaysElapsed);
        Assert.Equal((snapshot.PeriodEnd - Now).TotalDays, snapshot.DaysRemaining);
        Assert.Equal(snapshot.TxBytes / 4, snapshot.AverageBytesPerDay);
        Assert.Equal(snapshot.RemainingBytes / snapshot.DaysRemaining, snapshot.AvailableBytesPerDay);
        Assert.Equal(snapshot.TxBytes / 4 * (snapshot.PeriodEnd - snapshot.PeriodStart).TotalDays,
            snapshot.ProjectedBytes, 4);
        Assert.Equal(snapshot.ProjectedBytes / snapshot.MonthlyLimitBytes * 100, snapshot.ProjectedPercent);
        Assert.Equal(Now, snapshot.UpdatedAt);
        Assert.Null(snapshot.RxBytesPerSecond);
        Assert.Null(snapshot.TxBytesPerSecond);
    }

    [Theory]
    [InlineData("1000 GB", 1073741824000d)]
    [InlineData(" 1.5 gb ", 1610612736d)]
    [InlineData("2GiB", 2147483648d)]
    [InlineData("1 TB", 1099511627776d)]
    [InlineData("1024 B", 1024d)]
    public void ParsesBinaryLimits(string text, double expected) =>
        Assert.Equal(expected, ServitroBandwidthClient.ParseLimit(text));

    [Theory]
    [InlineData("")]
    [InlineData("unlimited")]
    [InlineData("0 GB")]
    [InlineData("-1 GB")]
    [InlineData("1.2.3 GB")]
    [InlineData("1 XB")]
    [InlineData("999999999999999999999999 GB")]
    public void RejectsInvalidLimits(string text) =>
        Assert.Throws<JsonException>(() => ServitroBandwidthClient.ParseLimit(text));

    [Fact]
    public async Task RxAndTotalDoNotAffectQuotaAndOverageIsPreserved()
    {
        JsonNode body = JsonNode.Parse(Payload)!;
        JsonNode traffic = body["data"]!["state"]!["network"]!["primary"]!["traffic"]!;
        traffic["rx"] = 9000000000000d;
        traffic["tx"] = 2000000000000d;
        traffic.AsObject().Remove("total");
        using var http = CreateHttp((_, _) => Task.FromResult(Response(body.ToJsonString())));
        var snapshot = Assert.Single(await CreateClient(http).GetAllAsync()).Snapshot!;
        Assert.Equal(2000000000000d, snapshot.UsedBytes);
        Assert.Equal(9000000000000d, snapshot.RxBytes);
        Assert.Equal(0, snapshot.RemainingBytes);
        Assert.True(snapshot.UsedPercent > 100);
    }

    [Theory]
    [InlineData(401, "rejected")]
    [InlineData(429, "rate limited")]
    [InlineData(503, "temporarily unavailable")]
    [InlineData(500, "could not be retrieved")]
    public async Task HttpFailuresAreSanitized(int status, string expected)
    {
        using var http = CreateHttp((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent("test-token private-response https://private.example")
        }));
        var node = Assert.Single(await CreateClient(http).GetAllAsync());
        Assert.Null(node.Snapshot);
        Assert.Contains(expected, node.Error);
        Assert.DoesNotContain("test-token", node.Error);
        Assert.DoesNotContain("private", node.Error);
    }

    [Theory]
    [InlineData("not-json test-token")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"data\":null}")]
    public async Task MalformedResponsesAreSanitized(string body)
    {
        using var http = CreateHttp((_, _) => Task.FromResult(Response(body)));
        var node = Assert.Single(await CreateClient(http).GetAllAsync());
        Assert.Null(node.Snapshot);
        Assert.Equal("Servitro returned invalid or incomplete bandwidth data.", node.Error);
    }

    [Theory]
    [InlineData("rx", "null")]
    [InlineData("tx", "-1")]
    [InlineData("tx", "1e400")]
    [InlineData("tx", "\"invalid\"")]
    [InlineData("limit", "\"secret invalid limit\"")]
    [InlineData("start", "\"bad-date\"")]
    [InlineData("end", "\"2026-10-01T00:00:00Z\"")]
    public async Task InvalidRequiredFieldsFailWithoutReturningPartialSnapshot(string field, string value)
    {
        JsonNode body = JsonNode.Parse(Payload)!;
        JsonNode parent = field switch
        {
            "rx" or "tx" => body["data"]!["state"]!["network"]!["primary"]!["traffic"]!,
            "limit" => body["data"]!["network"]!["primary"]!,
            _ => body["data"]!["currentMonthlyPeriod"]!
        };
        parent[field] = JsonNode.Parse(value);
        using var http = CreateHttp((_, _) => Task.FromResult(Response(body.ToJsonString())));
        var node = Assert.Single(await CreateClient(http).GetAllAsync());
        Assert.Null(node.Snapshot);
        Assert.Equal("Servitro returned invalid or incomplete bandwidth data.", node.Error);
    }

    [Fact]
    public async Task CancellationPropagatesAndRefreshGateCanBeUsedAgain()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = CreateHttp(async (_, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Response();
        });
        var options = Options.Create(CreateOptions());
        var controller = new ServitroBandwidthFleetRefreshController(CreateClient(http), options);
        using var cancellation = new CancellationTokenSource();
        Task<bool> refresh = controller.RefreshAsync(cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(await controller.RefreshAsync(CancellationToken.None));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        Assert.False(controller.IsRefreshing);
        Assert.Null(Assert.Single(controller.CurrentNodes).Snapshot);
    }

    [Fact]
    public async Task TimeoutIsDegradedAndDisabledProviderDoesNotSendRequests()
    {
        int calls = 0;
        using var http = CreateHttp((_, _) =>
        {
            calls++;
            throw new TaskCanceledException("secret private url");
        });
        var node = Assert.Single(await CreateClient(http).GetAllAsync());
        Assert.Equal("The Servitro bandwidth request timed out.", node.Error);
        var options = CreateOptions();
        options.Enabled = false;
        Assert.Empty(await CreateClient(http, options).GetAllAsync());
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task PartialFailureRetainsLastKnownAndDoesNotAffectOtherProvider()
    {
        bool fail = false;
        using var http = CreateHttp((request, _) => Task.FromResult(
            fail && request.RequestUri!.AbsolutePath.EndsWith("server%2Fid", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : Response(fail ? Payload.Replace("1085227514", "2085227514") : Payload)));
        var options = CreateOptions();
        options.Servers.Add(new() { NodeId = "second-node", ServerId = "second-server" });
        var servitro = new ServitroBandwidthFleetRefreshController(CreateClient(http, options), Options.Create(options));
        using var greenHttp = CreateHttp((_, _) => Task.FromResult(Response()));
        var greenOptions = Options.Create(new GreenCloudOptions
        {
            Enabled = true,
            ApiToken = "test-green-token",
            Servers = [new() { NodeId = "green-node", ServerId = "green-server" }]
        });
        var green = new GreenCloudBandwidthFleetRefreshController(
            new GreenCloudBandwidthClient(greenHttp, greenOptions, new(), new FixedTimeProvider()), greenOptions);
        var fleet = new BandwidthFleetState([green, servitro]);
        Assert.Equal(3, fleet.CurrentNodes.Count);
        await green.RefreshAsync(CancellationToken.None);
        await servitro.RefreshAsync(CancellationToken.None);
        var previous = servitro.CurrentNodes[0].Snapshot;
        var greenPrevious = green.CurrentNodes[0].Snapshot;
        Assert.Equal(6802238040d, greenPrevious!.UsedBytes); // GreenCloud retains total-traffic semantics.
        fail = true;
        await servitro.RefreshAsync(CancellationToken.None);
        Assert.Same(previous, servitro.CurrentNodes[0].Snapshot);
        Assert.NotNull(servitro.CurrentNodes[0].Error);
        Assert.Equal(2085227514d, servitro.CurrentNodes[1].Snapshot!.UsedBytes);
        Assert.Null(servitro.CurrentNodes[1].Error);
        Assert.Same(greenPrevious, fleet.CurrentNodes.Single(node => node.NodeId == "green-node").Snapshot);
        Assert.Null(green.CurrentNodes[0].Error);
    }

    [Fact]
    public async Task PollingUsesServitroIntervalAndRefreshesOnStartup()
    {
        int calls = 0;
        using var http = CreateHttp((_, _) => { calls++; return Task.FromResult(Response()); });
        var options = CreateOptions();
        options.PollSeconds = 417;
        var controller = new ServitroBandwidthFleetRefreshController(CreateClient(http, options), Options.Create(options));
        var loop = new CapturingPollingLoop();
        using var service = new ServitroBandwidthPollingService(controller, Options.Create(options), loop);
        await service.StartAsync(CancellationToken.None);
        await loop.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync(CancellationToken.None);
        Assert.Equal(1, calls);
        Assert.Equal(TimeSpan.FromSeconds(417), loop.Interval);
    }

    private static ServitroOptions CreateOptions() => new()
    {
        Enabled = true,
        Token = "test-token",
        Servers = [new() { NodeId = "custom-node", ServerId = "server/id" }]
    };

    private static ServitroBandwidthClient CreateClient(HttpClient http, ServitroOptions? options = null) =>
        new(http, Options.Create(options ?? CreateOptions()), new(), new FixedTimeProvider());

    private static HttpClient CreateHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) =>
        new(new Handler(send)) { BaseAddress = new Uri("https://platform.servitro.com/api/") };

    private static HttpResponseMessage Response(string body = Payload) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class CapturingPollingLoop : IDashboardPollingLoop
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TimeSpan Interval { get; private set; }
        public Task RunAsync(TimeSpan interval, Func<CancellationToken, Task> onTick, CancellationToken cancellationToken)
        {
            Interval = interval;
            Started.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
