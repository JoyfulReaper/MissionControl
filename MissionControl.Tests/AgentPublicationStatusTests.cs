extern alias AgentApp;

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentApp::MissionControl.Agent;
using AgentApp::MissionControl.Agent.Docker;
using AgentApp::MissionControl.Agent.Endpoints;
using AgentApp::MissionControl.Agent.Host;
using AgentApp::MissionControl.Agent.Models;
using AgentApp::MissionControl.Agent.Protocols;
using AgentApp::MissionControl.Agent.Publishing;
using AgentApp::MissionControl.Agent.Storage;
using JoyfulReaperLib.MissionControl;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MissionControl.Contracts.Agent;
using MissionControl.UI.Components.Services;
using Xunit;

namespace MissionControl.Tests;

public sealed class AgentPublicationStatusTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(true, true, "LAST ATTEMPT SUCCEEDED")]
    [InlineData(true, false, "LAST ATTEMPT FAILED")]
    [InlineData(false, true, "DISABLED")]
    public async Task WorkerPersistsExplicitConfigurationSeparatelyFromActualAttempts(
        bool enabled, bool succeeds, string expectedLabel)
    {
        await using var fixture = await AgentSnapshotStoreFixture.CreateAsync();
        using var handler = new PublishHandler(succeeds);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://gateway.example/") };
        var publicationOptions = Options.Create(new MissionControlClientOptions { Enabled = enabled });
        var client = new MissionControlClient(new ClientFactory(http), publicationOptions,
            NullLogger<MissionControlClient>.Instance);
        using var worker = CreateWorker(fixture.SnapshotStore, client, publicationOptions);

        await worker.ExecuteIterationAsync(CreateOptions(), CancellationToken.None);
        StoredNodeSnapshot first = (await fixture.SnapshotStore.GetAsync("test-node"))!;
        Assert.Equal(enabled, first.Snapshot.MissionControlPublicationEnabled);
        Assert.Equal(enabled ? (bool?)succeeds : null, first.PublishSucceeded);
        if (enabled)
        {
            Assert.NotNull(first.LastPublishAttemptAt);
        }
        else
        {
            Assert.Null(first.LastPublishAttemptAt);
        }

        await worker.ExecuteIterationAsync(CreateOptions(), CancellationToken.None);
        // Failed attempts remain due; successful attempts are suppressed; disabled never sends.
        Assert.Equal(!enabled ? 0 : succeeds ? 1 : 2, handler.CallCount);
        var reopenedStore = new SqliteNodeSnapshotStore(fixture.Database);
        StoredNodeSnapshot stored = (await reopenedStore.GetAsync("test-node"))!;
        Assert.Equal(enabled, stored.Snapshot.MissionControlPublicationEnabled);
        if (!enabled || succeeds)
        {
            Assert.Equal(first.LastPublishAttemptAt, stored.LastPublishAttemptAt);
        }

        PublicNodeSnapshot projected = RoundTripProjection(stored);
        Assert.Equal(enabled, projected.MissionControlPublicationEnabled);
        Assert.Equal(stored.PublishSucceeded, projected.MissionControlPublishSucceeded);
        Assert.Equal(stored.LastPublishAttemptAt, projected.LastMissionControlPublishAttemptAt);
        Assert.Equal(!enabled ? "status-unknown" : succeeds ? "status-running" : "status-stopped",
            PublicationStatusPresentation.GetCssClass(projected.MissionControlPublishSucceeded,
                projected.MissionControlPublicationEnabled));
        string html = await RenderPanelAsync(projected);
        Assert.Contains(expectedLabel, html);
        if (!enabled)
        {
            Assert.DoesNotContain("Last publish attempt", html);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DisablementPreservesRealAttemptHistoryAndReenablementCanPublish(bool previousSucceeded)
    {
        await using var fixture = await AgentSnapshotStoreFixture.CreateAsync();
        DateTimeOffset previousAttempt = DateTimeOffset.UtcNow.AddMinutes(-10);
        await fixture.SnapshotStore.SaveAsync(CreateSnapshot(previousAttempt.AddSeconds(-1)), CancellationToken.None);
        await fixture.SnapshotStore.RecordPublishResultAsync("test-node", previousSucceeded, previousAttempt, CancellationToken.None);
        using var handler = new PublishHandler(true);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://gateway.example/") };
        var options = Options.Create(new MissionControlClientOptions { Enabled = false });
        var client = new MissionControlClient(new ClientFactory(http), options, NullLogger<MissionControlClient>.Instance);
        using var worker = CreateWorker(fixture.SnapshotStore, client, options);
        await worker.ExecuteIterationAsync(CreateOptions(), CancellationToken.None);
        await worker.ExecuteIterationAsync(CreateOptions(), CancellationToken.None);

        StoredNodeSnapshot stored = (await fixture.SnapshotStore.GetAsync("test-node"))!;
        Assert.False(stored.Snapshot.MissionControlPublicationEnabled);
        Assert.Equal(previousSucceeded, stored.PublishSucceeded);
        Assert.Equal(previousAttempt, stored.LastPublishAttemptAt);
        Assert.Equal(0, handler.CallCount);
        string html = await RenderPanelAsync(RoundTripProjection(stored));
        Assert.Contains("DISABLED", html);
        Assert.DoesNotContain("LAST ATTEMPT FAILED", html);
        Assert.DoesNotContain("LAST ATTEMPT SUCCEEDED", html);

        // A process restarted with enabled configuration must attempt publication normally.
        var enabledOptions = Options.Create(new MissionControlClientOptions { Enabled = true });
        var enabledClient = new MissionControlClient(new ClientFactory(http), enabledOptions, NullLogger<MissionControlClient>.Instance);
        using var enabledWorker = CreateWorker(fixture.SnapshotStore, enabledClient, enabledOptions);
        await enabledWorker.ExecuteIterationAsync(CreateOptions(), CancellationToken.None);
        stored = (await fixture.SnapshotStore.GetAsync("test-node"))!;
        Assert.True(stored.Snapshot.MissionControlPublicationEnabled);
        Assert.True(stored.PublishSucceeded);
        Assert.True(stored.LastPublishAttemptAt > previousAttempt);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task EnabledSnapshotWithoutAttemptRemainsNeverAttempted()
    {
        await using var fixture = await AgentSnapshotStoreFixture.CreateAsync();
        await fixture.SnapshotStore.SaveAsync(CreateSnapshot(DateTimeOffset.UtcNow), CancellationToken.None);
        StoredNodeSnapshot stored = (await fixture.SnapshotStore.GetAsync("test-node"))!;
        Assert.True(stored.Snapshot.MissionControlPublicationEnabled);
        Assert.Null(stored.PublishSucceeded);
        Assert.Null(stored.LastPublishAttemptAt);
        string html = await RenderPanelAsync(RoundTripProjection(stored));
        Assert.Contains("NO PUBLISH ATTEMPT RECORDED", html);
        Assert.DoesNotContain("DISABLED", html);
        Assert.DoesNotContain("Last publish attempt", html);
    }

    [Theory]
    [InlineData(null, "NO PUBLISH ATTEMPT RECORDED")]
    [InlineData(true, "LAST ATTEMPT SUCCEEDED")]
    [InlineData(false, "LAST ATTEMPT FAILED")]
    public async Task OldSnapshotJsonDoesNotImplyDisablement(bool? succeeded, string label)
    {
        JsonObject oldPayload = JsonSerializer.SerializeToNode(CreateSnapshot(DateTimeOffset.UtcNow), JsonOptions)!.AsObject();
        oldPayload.Remove("missionControlPublicationEnabled");
        NodeSnapshotEvent oldSnapshot = oldPayload.Deserialize<NodeSnapshotEvent>(JsonOptions)!;
        Assert.Null(oldSnapshot.MissionControlPublicationEnabled);
        await using var fixture = await AgentSnapshotStoreFixture.CreateAsync();
        await fixture.SnapshotStore.SaveAsync(oldSnapshot, CancellationToken.None);
        if (succeeded.HasValue)
        {
            await fixture.SnapshotStore.RecordPublishResultAsync("test-node", succeeded.Value,
                DateTimeOffset.UtcNow, CancellationToken.None);
        }

        StoredNodeSnapshot stored = (await fixture.SnapshotStore.GetAsync("test-node"))!;
        Assert.Null(stored.Snapshot.MissionControlPublicationEnabled);
        JsonObject oldPublicJson = JsonSerializer.SerializeToNode(RoundTripProjection(stored), JsonOptions)!.AsObject();
        oldPublicJson.Remove("missionControlPublicationEnabled");
        PublicNodeSnapshot oldPublicSnapshot = oldPublicJson.Deserialize<PublicNodeSnapshot>(JsonOptions)!;
        Assert.Null(oldPublicSnapshot.MissionControlPublicationEnabled);
        Assert.Contains(label, await RenderPanelAsync(oldPublicSnapshot));
    }

    [Fact]
    public async Task InstalledClientReturnsFalseWhenDisabledWithoutMakingAnHttpRequest()
    {
        using var handler = new PublishHandler(true);
        using var http = new HttpClient(handler);
        var client = new MissionControlClient(new ClientFactory(http),
            Options.Create(new MissionControlClientOptions { Enabled = false }),
            NullLogger<MissionControlClient>.Instance);
        NodeSnapshotEvent snapshot = CreateSnapshot(DateTimeOffset.UtcNow);
        bool result = await client.TryPublishAsync("missioncontrol.agent.node.snapshot", snapshot,
            AgentEventJsonContext.Default.NodeSnapshotEvent, snapshot.CapturedAt);
        Assert.False(result);
        Assert.Equal(0, handler.CallCount);
    }

    private static AgentOptions CreateOptions() => new()
    {
        NodeId = "test-node",
        NodeName = "Test Node",
        DockerEnabled = false,
        Probes = []
    };

    private static NodeSnapshotEvent CreateSnapshot(DateTimeOffset capturedAt) =>
        new("Test Node", capturedAt, null, [], [])
        {
            NodeId = "test-node",
            MissionControlPublicationEnabled = true
        };

    private static AgentWorker CreateWorker(INodeSnapshotStore store, IMissionControlClient client,
        IOptions<MissionControlClientOptions> publicationOptions) =>
        new(NullLogger<AgentWorker>.Instance, new UnusedDockerCollector(), new HostCollector(), client,
            new ProtocolProbeRunner([]), store, new SnapshotPublicationGate(TimeSpan.FromHours(1)),
            Options.Create(CreateOptions()), publicationOptions);

    private static PublicNodeSnapshot RoundTripProjection(StoredNodeSnapshot stored) =>
        JsonSerializer.Deserialize<PublicNodeSnapshot>(JsonSerializer.Serialize(
            AgentSnapshotEndpointRouteBuilderExtensions.CreatePublicSnapshot(
                stored, DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)), JsonOptions), JsonOptions)!;

    private static async Task<string> RenderPanelAsync(PublicNodeSnapshot snapshot)
    {
        using ServiceProvider services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(AgentSnapshotPanel.Snapshot)] = snapshot,
                [nameof(AgentSnapshotPanel.TimestampFormatter)] = (Func<DateTimeOffset, string>)(time => time.ToString("O"))
            });
            var component = await renderer.RenderComponentAsync<AgentSnapshotPanel>(parameters);
            return component.ToHtmlString();
        });
    }

    private sealed class PublishHandler(bool succeeds) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(succeeds ? HttpStatusCode.Accepted : HttpStatusCode.ServiceUnavailable));
        }
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class UnusedDockerCollector : IDockerMetricsCollector
    {
        public Task<DockerMetricsCollectionResult> GetMetricsAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Docker collection is disabled in this test.");
    }

    private sealed class HostCollector : IHostMetricsCollector
    {
        public Task<HostMetric> GetMetricsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new HostMetric(2, 10, 2000, 1000));
    }
}
