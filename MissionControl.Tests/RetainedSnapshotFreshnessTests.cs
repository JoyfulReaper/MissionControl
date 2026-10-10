extern alias DashboardApp;

using DashboardApp::MissionControl.Dashboard.Agents;
using DashboardApp::MissionControl.Dashboard.Refresh;
using MissionControl.Client.Infrastructure;
using MissionControl.Contracts.Agent;
using MissionControl.Mobile.Services;
using Xunit;

namespace MissionControl.Tests;

public sealed class RetainedSnapshotFreshnessTests
{
    [Fact]
    public async Task DashboardReadsAgeRetainedSnapshotsWithoutFetching()
    {
        var time = new ManualTime();
        PublicNodeSnapshot original = Snapshot(time.GetUtcNow());
        var client = new FleetClient { Nodes = [new("first", "First", original, null)] };
        var state = new AgentFleetRefreshController(client, time, TimeSpan.FromSeconds(60));
        await state.RefreshAsync(default);
        time.Now = original.CapturedAt.AddSeconds(30);
        client.Nodes = [new("first", "First", null, "offline"), new("second", "Second", Snapshot(time.Now), null)];
        await state.RefreshAsync(default);
        AgentNodeResult fresh = state.CurrentNodes[0];
        Assert.False(fresh.Snapshot!.Stale);
        Assert.Equal(30, fresh.Snapshot.AgeSeconds);
        time.Now = original.CapturedAt.AddSeconds(60);
        Assert.False(state.CurrentNodes[0].Snapshot!.Stale);
        time.Now = original.CapturedAt.AddSeconds(61);
        AgentNodeResult stale = state.CurrentNodes[0];
        Assert.True(stale.Snapshot!.Stale);
        Assert.Equal(61, stale.Snapshot.AgeSeconds);
        Assert.Equal(original.CapturedAt, stale.Snapshot.CapturedAt);
        Assert.Equal("offline", stale.Error);
        Assert.False(stale.Succeeded);
        Assert.False(state.CurrentNodes[1].Snapshot!.Stale);
        Assert.Equal(2, client.Calls);
        Assert.Equal(0, original.AgeSeconds);
        Assert.False(original.Stale);
        Assert.Equal(30, fresh.Snapshot.AgeSeconds);
        client.Nodes = [new("first", "First", null, "still offline"), new("second", "Second", Snapshot(time.Now), null)];
        await state.RefreshAsync(default);
        Assert.True(state.CurrentNodes[0].Snapshot!.Stale);
        Assert.Equal("still offline", state.CurrentNodes[0].Error);
        Assert.Equal(original.CapturedAt, state.CurrentNodes[0].Snapshot!.CapturedAt);
        Assert.Equal(0, state.CurrentNodes[1].Snapshot!.AgeSeconds);
        Assert.True(state.CurrentNodes[1].Succeeded);
        client.Nodes = [new("first", "First", Snapshot(time.Now), null), new("second", "Second", Snapshot(time.Now), null)];
        await state.RefreshAsync(default);
        Assert.All(state.CurrentNodes, node =>
        {
            Assert.True(node.Succeeded);
            Assert.False(node.Snapshot!.Stale);
            Assert.Equal(0, node.Snapshot.AgeSeconds);
        });
    }

    [Fact]
    public async Task MobileReadsAgeRetainedSnapshotsUsingServerThresholdWithoutFetching()
    {
        var time = new ManualTime();
        PublicNodeSnapshot original = Snapshot(time.Now);
        var client = new HostClient
        {
            Hosts = [new HostNodeSnapshot("first", "First", original, "Agent unavailable", false, null, null)
            {
                SnapshotStaleAfterSeconds = 60
            }]
        };
        using var state = new MobileHostFleetState(client, time);
        await state.RefreshAsync(false);
        time.Now = original.CapturedAt.AddSeconds(30);
        client.Fail = true;
        await state.RefreshAsync(false);
        Assert.NotNull(state.ErrorMessage);
        Assert.False(state.Hosts[0].AgentSnapshot!.Stale);
        Assert.Equal(30, state.Hosts[0].AgentSnapshot!.AgeSeconds);
        time.Now = original.CapturedAt.AddSeconds(61);
        HostNodeSnapshot retained = state.Hosts[0];
        Assert.True(retained.AgentSnapshot!.Stale);
        Assert.Equal(61, retained.AgentSnapshot.AgeSeconds);
        Assert.Equal(original.CapturedAt, retained.AgentSnapshot.CapturedAt);
        Assert.Equal("Agent unavailable", retained.AgentError);
        Assert.Equal(2, client.Calls);
        Assert.False(original.Stale);
        Assert.Equal(0, original.AgeSeconds);
        client.Fail = false;
        client.Hosts = [client.Hosts[0] with { AgentSnapshot = Snapshot(time.Now), AgentError = null }];
        await state.RefreshAsync(false);
        Assert.Null(state.ErrorMessage);
        Assert.Null(state.Hosts[0].AgentError);
        Assert.False(state.Hosts[0].AgentSnapshot!.Stale);
        Assert.Equal(0, state.Hosts[0].AgentSnapshot!.AgeSeconds);
    }

    private static PublicNodeSnapshot Snapshot(DateTimeOffset at) => new("Node", at, 0, false, null, null, null, [], []);

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 10, 14, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FleetClient : IAgentFleetClient
    {
        public IReadOnlyList<AgentNodeResult> Nodes { get; set; } = [];
        public int Calls { get; private set; }
        public Task<IReadOnlyList<AgentNodeResult>> GetSnapshotsAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Nodes);
        }
    }

    private sealed class HostClient : IHostFleetClient
    {
        public IReadOnlyList<HostNodeSnapshot> Hosts { get; set; } = [];
        public int Calls { get; private set; }
        public bool Fail { get; set; }
        public Task<IReadOnlyList<HostNodeSnapshot>> GetAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Fail ? Task.FromException<IReadOnlyList<HostNodeSnapshot>>(new HttpRequestException()) : Task.FromResult(Hosts);
        }
    }
}
