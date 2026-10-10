extern alias DashboardApp;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DashboardApp::MissionControl.Dashboard.Agents;
using DashboardApp::MissionControl.Dashboard.Refresh;
using Microsoft.Extensions.Options;
using MissionControl.Contracts.Agent;
using Xunit;

namespace MissionControl.Tests;

public sealed class AgentFleetClientTests
{
    [Theory]
    [InlineData("not JSON private-response-secret")]
    [InlineData("{\"nodeId\":\"first\",")]
    [InlineData("{\"nodeId\":\"first\",\"ageSeconds\":\"private-response-secret\"}")]
    [InlineData("[]")]
    [InlineData("")]
    [InlineData("null")]
    public async Task UnreadableResponseFailsOnlyItsNode(string body)
    {
        using var factory = new SnapshotFactory(_ => Snapshot("second"))
        {
            Content = host => host == "first.example" ? new StringContent(body) : JsonContent.Create(Snapshot("second"))
        };
        IReadOnlyList<AgentNodeResult> nodes = await CreateClient(factory).GetSnapshotsAsync();
        Assert.Null(nodes[0].Snapshot);
        Assert.False(nodes[0].Succeeded);
        Assert.Contains("Configured node 'first'", nodes[0].Error);
        Assert.DoesNotContain("identity mismatch", nodes[0].Error);
        Assert.DoesNotContain("private", nodes[0].Error);
        Assert.DoesNotContain("example", nodes[0].Error);
        Assert.True(nodes[0].Error!.Length < 200);
        Assert.True(nodes[1].Succeeded);
        Assert.Equal("second", nodes[1].Snapshot!.NodeId);
    }

    [Fact]
    public async Task AllMalformedNodesProduceIndependentErrors()
    {
        using var factory = new SnapshotFactory(_ => Snapshot(null))
        {
            Content = _ => new StringContent("{")
        };
        var controller = new AgentFleetRefreshController(CreateClient(factory), TimeProvider.System, TimeSpan.FromMinutes(2));
        await controller.RefreshAsync(CancellationToken.None);
        Assert.Equal(2, controller.CurrentNodes.Count);
        Assert.All(controller.CurrentNodes, node =>
        {
            Assert.Null(node.Snapshot);
            Assert.False(node.Succeeded);
            Assert.Contains($"Configured node '{node.NodeId}'", node.Error);
            Assert.Contains("invalid or incompatible JSON", node.Error);
        });
        Assert.Null(controller.RefreshWarning);
    }

    [Fact]
    public async Task MalformedResponseRetainsPreviousSnapshotWhileHealthyNodeUpdates()
    {
        int refresh = 0;
        DateTimeOffset initial = DateTimeOffset.UtcNow.AddMinutes(-10);
        using var factory = new SnapshotFactory(_ => Snapshot(null))
        {
            Content = host => refresh == 1 && host == "first.example"
                ? new StringContent("{")
                : JsonContent.Create(Snapshot(host == "first.example" ? "first" : "second") with
                {
                    CapturedAt = initial.AddSeconds(refresh)
                })
        };
        var controller = new AgentFleetRefreshController(CreateClient(factory), TimeProvider.System, TimeSpan.FromMinutes(2));
        await controller.RefreshAsync(CancellationToken.None);
        refresh = 1;
        await controller.RefreshAsync(CancellationToken.None);
        Assert.Equal(initial, controller.CurrentNodes[0].Snapshot!.CapturedAt);
        Assert.Equal("first", controller.CurrentNodes[0].Snapshot!.NodeId);
        Assert.True(controller.CurrentNodes[0].Snapshot!.Stale);
        Assert.Contains("invalid or incompatible JSON", controller.CurrentNodes[0].Error);
        Assert.Equal(initial.AddSeconds(1), controller.CurrentNodes[1].Snapshot!.CapturedAt);
        Assert.True(controller.CurrentNodes[1].Succeeded);
        Assert.Null(controller.RefreshWarning);
        refresh = 2;
        await controller.RefreshAsync(CancellationToken.None);
        Assert.True(controller.CurrentNodes[0].Succeeded);
        Assert.Equal(initial.AddSeconds(2), controller.CurrentNodes[0].Snapshot!.CapturedAt);
    }

    [Fact]
    public async Task CancellationWhileReadingResponsePropagatesThroughRefresh()
    {
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var factory = new SnapshotFactory(_ => Snapshot(null))
        {
            Content = host => host == "first.example"
                ? new BlockingContent(started)
                : JsonContent.Create(Snapshot("second"))
        };
        var controller = new AgentFleetRefreshController(CreateClient(factory), TimeProvider.System, TimeSpan.FromMinutes(2));
        Task refresh = controller.RefreshAsync(cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        Assert.Null(controller.RefreshWarning);
        Assert.False(controller.IsRefreshing);
    }

    private sealed class BlockingContent(TaskCompletionSource started) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(
            Stream stream, TransportContext? context, CancellationToken cancellationToken)
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    [Fact]
    public async Task UnreadableStreamIsIsolatedAndSanitized()
    {
        using var factory = new SnapshotFactory(host => host == "first.example"
            ? throw new IOException("private-response-secret")
            : Snapshot("second"));
        IReadOnlyList<AgentNodeResult> nodes = await CreateClient(factory).GetSnapshotsAsync();
        Assert.Null(nodes[0].Snapshot);
        Assert.Contains("could not be read", nodes[0].Error);
        Assert.DoesNotContain("private", nodes[0].Error);
        Assert.True(nodes[1].Succeeded);
    }

    [Theory]
    [InlineData("first")]
    [InlineData("FIRST")]
    public async Task MatchingIdentityIsAcceptedWithoutRewriting(string reported)
    {
        using var factory = new SnapshotFactory(_ => Snapshot(reported));
        AgentNodeResult result = (await CreateClient(factory).GetSnapshotsAsync())[0];
        Assert.True(result.Succeeded);
        Assert.Equal(reported, result.Snapshot!.NodeId);
        Assert.Equal("private-display-name", result.Snapshot.Node);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData("other", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" \t", false)]
    [InlineData(null, true)]
    public async Task InvalidIdentityFailsOnlyItsNode(string? reported, bool omitIdentity)
    {
        using var factory = new SnapshotFactory(host => Snapshot(host == "first.example" ? reported : "second"))
        {
            OmitFirstIdentity = omitIdentity
        };
        IReadOnlyList<AgentNodeResult> nodes = await CreateClient(factory).GetSnapshotsAsync();
        Assert.False(nodes[0].Succeeded);
        Assert.Null(nodes[0].Snapshot);
        Assert.Contains("configured node 'first'", nodes[0].Error);
        Assert.Contains(string.IsNullOrWhiteSpace(reported) ? "missing or blank NodeId" : "NodeId 'other'", nodes[0].Error);
        Assert.DoesNotContain("private", nodes[0].Error);
        Assert.DoesNotContain("example", nodes[0].Error);
        Assert.True(nodes[1].Succeeded);
        Assert.Equal("second", nodes[1].Snapshot!.NodeId);
    }

    [Fact]
    public async Task IdentityDiagnosticIsBoundedAndSingleLine()
    {
        using var factory = new SnapshotFactory(_ => Snapshot("other\r\n" + new string('x', 300)));
        AgentNodeResult result = (await CreateClient(factory).GetSnapshotsAsync())[0];
        Assert.Contains("other??", result.Error);
        Assert.DoesNotContain("\r", result.Error);
        Assert.DoesNotContain("\n", result.Error);
        Assert.True(result.Error!.Length < 250);
    }

    [Fact]
    public async Task RefreshRetainsGoodSnapshotAfterIdentityMismatchAndRecovers()
    {
        int refresh = 0;
        DateTimeOffset initial = DateTimeOffset.UtcNow.AddMinutes(-10);
        using var factory = new SnapshotFactory(host => Snapshot(
            host == "first.example" ? (refresh == 1 ? "other" : "first") : "second") with
        {
            CapturedAt = initial.AddSeconds(refresh)
        });
        var controller = new AgentFleetRefreshController(CreateClient(factory), TimeProvider.System, TimeSpan.FromMinutes(2));
        await controller.RefreshAsync(CancellationToken.None);
        refresh = 1;
        await controller.RefreshAsync(CancellationToken.None);
        AgentNodeResult failed = controller.CurrentNodes[0];
        Assert.Equal("first", failed.Snapshot!.NodeId);
        Assert.Equal(initial, failed.Snapshot.CapturedAt);
        Assert.True(failed.Snapshot.Stale);
        Assert.False(failed.Succeeded);
        Assert.Contains("NodeId 'other'", failed.Error);
        Assert.Equal(initial.AddSeconds(1), controller.CurrentNodes[1].Snapshot!.CapturedAt);
        Assert.True(controller.CurrentNodes[1].Succeeded);
        Assert.Null(controller.RefreshWarning);
        refresh = 2;
        await controller.RefreshAsync(CancellationToken.None);
        Assert.True(controller.CurrentNodes[0].Succeeded);
        Assert.Equal(initial.AddSeconds(2), controller.CurrentNodes[0].Snapshot!.CapturedAt);
        Assert.Null(controller.CurrentNodes[0].Error);
    }

    [Fact]
    public async Task CallerCancellationStillPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        using var factory = new SnapshotFactory(_ =>
        {
            cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
            return Snapshot("first");
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateClient(factory).GetSnapshotsAsync(cancellation.Token));
    }

    [Fact]
    public async Task TimeoutStillFailsOnlyItsNode()
    {
        using var factory = new SnapshotFactory(host => host == "first.example"
            ? throw new TaskCanceledException("Timed out")
            : Snapshot("second"));
        IReadOnlyList<AgentNodeResult> nodes = await CreateClient(factory).GetSnapshotsAsync();
        Assert.Null(nodes[0].Snapshot);
        Assert.Contains("Timed out", nodes[0].Error);
        Assert.True(nodes[1].Succeeded);
    }

    private static AgentFleetClient CreateClient(IHttpClientFactory factory) => new(factory,
        Options.Create(new AgentNodesOptions
        {
            Nodes =
            [
                new() { NodeId = "first", DisplayName = "First", BaseUrl = "https://first.example/private/" },
                new() { NodeId = "second", DisplayName = "Second", BaseUrl = "https://second.example/" }
            ]
        }));

    private static PublicNodeSnapshot Snapshot(string? nodeId) => new(
        "private-display-name", DateTimeOffset.UtcNow, 0, false, null, null, null, [], [],
        DockerError: "private-provider-details")
    {
        NodeId = nodeId
    };

    private sealed class SnapshotFactory(Func<string, PublicNodeSnapshot> snapshot) : HttpMessageHandler, IHttpClientFactory
    {
        private readonly List<HttpClient> clients = [];
        public bool OmitFirstIdentity { get; init; }
        public Func<string, HttpContent>? Content { get; init; }

        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(this, disposeHandler: false);
            clients.Add(client);
            return client;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Content is not null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = Content(request.RequestUri!.Host)
                });
            }

            JsonNode body = JsonSerializer.SerializeToNode(snapshot(request.RequestUri!.Host))!;
            if (OmitFirstIdentity && request.RequestUri.Host == "first.example")
            {
                body.AsObject().Remove("NodeId");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (HttpClient client in clients)
                {
                    client.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}
