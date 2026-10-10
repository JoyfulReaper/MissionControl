extern alias DashboardApp;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using DashboardApp::MissionControl.Dashboard.GreenCloud;
using Microsoft.Extensions.Options;
using Xunit;

namespace MissionControl.Tests;

public sealed class GreenCloudBandwidthClientTests
{
    [Fact]
    public async Task FleetUsesEachCanonicalNodeAndProviderIdAndRatesUseNodeId()
    {
        using var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://greencloud.example/") };
        var time = new FixedTimeProvider();
        var rates = new GreenCloudBandwidthRateState();
        rates.Update("first-node", 100, 200, time.GetUtcNow().AddSeconds(-10));
        var options = new GreenCloudOptions
        {
            Enabled = true,
            ApiToken = "test-only-token",
            Servers =
            [
                new() { NodeId = "first-node", ServerId = "provider-first" },
                new() { NodeId = "second-node", ServerId = "provider-second" }
            ]
        };
        var client = new GreenCloudBandwidthClient(http, Options.Create(options), rates, time);
        var nodes = await client.GetAllAsync();
        Assert.Equal(2, nodes.Count);
        Assert.All(nodes, node => Assert.True(node.Succeeded));
        Assert.Equal("first-node", nodes[0].NodeId);
        Assert.Equal("/api/server/provider-first", nodes[0].Snapshot!.ServerName);
        Assert.Equal(10, nodes[0].Snapshot!.RxBytesPerSecond);
        Assert.Equal(20, nodes[0].Snapshot!.TxBytesPerSecond);
        Assert.Equal("second-node", nodes[1].NodeId);
        Assert.Equal("/api/server/provider-second", nodes[1].Snapshot!.ServerName);
        Assert.Null(nodes[1].Snapshot!.RxBytesPerSecond);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task CompatibilityClientUsesTheSoleCanonicalMapping()
    {
        using var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://greencloud.example/") };
        var time = new FixedTimeProvider();
        var rates = new GreenCloudBandwidthRateState();
        rates.Update("explicit-node", 100, 200, time.GetUtcNow().AddSeconds(-10));
        var client = new GreenCloudBandwidthClient(http, Options.Create(new GreenCloudOptions
        {
            Enabled = true,
            ApiToken = "test-only-token",
            Servers = [new() { NodeId = "explicit-node", ServerId = "provider-id" }]
        }), rates, time);
        var snapshot = await client.GetAsync();
        Assert.Equal("/api/server/provider-id", snapshot.ServerName);
        Assert.Equal(10, snapshot.RxBytesPerSecond);
        Assert.Equal(20, snapshot.TxBytesPerSecond);
        Assert.Equal("https://greencloud.example/api/server/provider-id?state=true", Assert.Single(handler.Requests).AbsoluteUri);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    [InlineData(true, 2)]
    public async Task CompatibilityClientRejectsDisabledMissingOrAmbiguousSelection(bool enabled, int count)
    {
        using var handler = new RecordingHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://greencloud.example/") };
        var client = new GreenCloudBandwidthClient(http, Options.Create(new GreenCloudOptions
        {
            Enabled = enabled,
            Servers = Enumerable.Range(0, count).Select(index => new GreenCloudServerOptions
            {
                NodeId = $"node-{index}",
                ServerId = $"provider-{index}"
            }).ToList()
        }), new(), new FixedTimeProvider());
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync());
        Assert.Empty(handler.Requests);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.Parse("2026-10-10T00:00:00Z");
    }

    internal sealed class RecordingHandler : HttpMessageHandler
    {
        public ConcurrentBag<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-only-token", request.Headers.Authorization?.Parameter);
            Assert.Equal("application/json", Assert.Single(request.Headers.Accept).MediaType);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    data = new
                    {
                        name = request.RequestUri!.AbsolutePath,
                        network = new { primary = new { limit = "1000 GB" } },
                        state = new { status = "running", network = new { primary = new { traffic = new { rx = 200, tx = 400, total = 600 } } } },
                        currentMonthlyPeriod = new { start = "2026-10-01T00:00:00Z", end = "2026-11-01T00:00:00Z" }
                    }
                })
            });
        }
    }
}
