extern alias DashboardApp;

using DashboardApp::MissionControl.Dashboard.GreenCloud;
using Microsoft.Extensions.Options;
using MissionControl.Client.Infrastructure;
using Xunit;
using IDashboardPollingLoop =
    DashboardApp::MissionControl.Dashboard.Refresh.IDashboardPollingLoop;

namespace MissionControl.Tests;

public sealed class GreenCloudBandwidthFleetRefreshTests
{
    [Fact]
    public async Task FailedNodeRetainsPreviousSnapshot()
    {
        BandwidthUsageSnapshot clankerInitial =
            CreateSnapshot(
                "Clanker",
                usedBytes: 100);

        BandwidthUsageSnapshot scopeCreepInitial =
            CreateSnapshot(
                "ScopeCreep",
                usedBytes: 200);

        BandwidthUsageSnapshot clankerUpdated =
            CreateSnapshot(
                "Clanker",
                usedBytes: 150);

        var client =
            new QueueGreenCloudFleetClient(
            [
                [
                    new(
                        "clanker",
                        clankerInitial,
                        null),

                    new(
                        "scopecreep",
                        scopeCreepInitial,
                        null)
                ],
                [
                    new(
                        "clanker",
                        clankerUpdated,
                        null),

                    new(
                        "scopecreep",
                        null,
                        "offline")
                ]
            ]);

        var controller =
            new GreenCloudBandwidthFleetRefreshController(
                client,
                Options.Create(new GreenCloudOptions()));

        await controller.RefreshAsync(
            CancellationToken.None);

        await controller.RefreshAsync(
            CancellationToken.None);

        GreenCloudBandwidthNodeResult clanker =
            Assert.Single(
                controller.CurrentNodes,
                node => node.NodeId == "clanker");

        GreenCloudBandwidthNodeResult scopeCreep =
            Assert.Single(
                controller.CurrentNodes,
                node => node.NodeId == "scopecreep");

        Assert.Equal(
            150,
            clanker.Snapshot?.UsedBytes);

        Assert.True(clanker.Succeeded);

        Assert.Equal(
            200,
            scopeCreep.Snapshot?.UsedBytes);

        Assert.False(scopeCreep.Succeeded);
        Assert.Equal(
            "offline",
            scopeCreep.Error);
    }

    [Fact]
    public async Task FleetFailureRetainsPreviousSnapshotAndExposesError()
    {
        BandwidthUsageSnapshot initial =
            CreateSnapshot(
                "Clanker",
                usedBytes: 100);

        var client =
            new FailAfterSuccessGreenCloudFleetClient(
                [
                    new GreenCloudBandwidthNodeResult(
                        "clanker",
                        initial,
                        Error: null)
                ]);

        var controller =
            new GreenCloudBandwidthFleetRefreshController(
                client,
                Options.Create(new GreenCloudOptions()));

        await controller.RefreshAsync(CancellationToken.None);
        await controller.RefreshAsync(CancellationToken.None);

        GreenCloudBandwidthNodeResult node =
            Assert.Single(controller.CurrentNodes);

        Assert.Same(initial, node.Snapshot);
        Assert.Contains(
            "Latest GreenCloud refresh failed",
            node.Error);
        Assert.NotNull(controller.RefreshWarning);
    }

    [Fact]
    public async Task ConcurrentRefreshesOnlyCallProviderOnce()
    {
        var client = new BlockingGreenCloudFleetClient();

        var controller =
            new GreenCloudBandwidthFleetRefreshController(
                client,
                Options.Create(new GreenCloudOptions()));

        Task<bool> first =
            controller.RefreshAsync(CancellationToken.None);

        await client.RequestStarted;

        bool second =
            await controller.RefreshAsync(CancellationToken.None);

        Assert.False(second);
        Assert.Equal(1, client.CallCount);

        client.Release();

        Assert.True(await first);
    }

    [Fact]
    public async Task PollingServiceUsesConfiguredPollSeconds()
    {
        var client =
            new QueueGreenCloudFleetClient(
            [
                []
            ]);

        var options =
            Options.Create(
                new GreenCloudOptions
                {
                    Enabled = true,
                    PollSeconds = 417
                });

        var controller =
            new GreenCloudBandwidthFleetRefreshController(
                client,
                options);

        var pollingLoop = new CapturingPollingLoop();
        var service =
            new GreenCloudBandwidthPollingService(
                controller,
                options,
                pollingLoop);

        await service.StartAsync(CancellationToken.None);
        await pollingLoop.Started.WaitAsync(
            TimeSpan.FromSeconds(2));
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(1, client.CallCount);
        Assert.Equal(
            TimeSpan.FromSeconds(417),
            pollingLoop.Interval);
    }

    private static BandwidthUsageSnapshot CreateSnapshot(
        string serverName,
        double usedBytes)
    {
        DateTimeOffset now =
            new(
                2026,
                9,
                23,
                12,
                0,
                0,
                TimeSpan.Zero);

        return new BandwidthUsageSnapshot(
            ServerName: serverName,
            Status: "running",
            MonthlyLimitBytes: 1_000,
            RxBytes: usedBytes / 2,
            TxBytes: usedBytes / 2,
            UsedBytes: usedBytes,
            RemainingBytes: 1_000 - usedBytes,
            UsedPercent: usedBytes / 10,
            RemainingPercent: 100 - usedBytes / 10,
            PeriodStart: now.AddDays(-1),
            PeriodEnd: now.AddDays(29),
            DaysElapsed: 1,
            DaysRemaining: 29,
            AverageBytesPerDay: usedBytes,
            AvailableBytesPerDay:
                (1_000 - usedBytes) / 29,
            ProjectedBytes: usedBytes * 30,
            ProjectedPercent: usedBytes * 3,
            RxBytesPerSecond: null,
            TxBytesPerSecond: null,
            UpdatedAt: now);
    }

    private sealed class QueueGreenCloudFleetClient(
        IEnumerable<
            IReadOnlyList<
                GreenCloudBandwidthNodeResult>> responses)
        : IGreenCloudBandwidthFleetClient
    {
        private readonly Queue<
            IReadOnlyList<
                GreenCloudBandwidthNodeResult>>
            responses = new(responses);

        public int CallCount { get; private set; }

        public Task<
            IReadOnlyList<
                GreenCloudBandwidthNodeResult>>
            GetAllAsync(
                CancellationToken cancellationToken = default)
        {
            CallCount++;

            return Task.FromResult(
                responses.Dequeue());
        }
    }

    private sealed class FailAfterSuccessGreenCloudFleetClient(
        IReadOnlyList<GreenCloudBandwidthNodeResult> initial)
        : IGreenCloudBandwidthFleetClient
    {
        private int callCount;

        public Task<IReadOnlyList<GreenCloudBandwidthNodeResult>>
            GetAllAsync(
                CancellationToken cancellationToken = default)
        {
            callCount++;

            return callCount == 1
                ? Task.FromResult(initial)
                : Task.FromException<
                    IReadOnlyList<GreenCloudBandwidthNodeResult>>(
                        new HttpRequestException("provider unavailable"));
        }
    }

    private sealed class BlockingGreenCloudFleetClient
        : IGreenCloudBandwidthFleetClient
    {
        private readonly TaskCompletionSource requestStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task RequestStarted => requestStarted.Task;

        public int CallCount { get; private set; }

        public async Task<
            IReadOnlyList<GreenCloudBandwidthNodeResult>>
            GetAllAsync(
                CancellationToken cancellationToken = default)
        {
            CallCount++;
            requestStarted.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);

            return [];
        }

        public void Release()
        {
            release.TrySetResult();
        }
    }

    private sealed class CapturingPollingLoop : IDashboardPollingLoop
    {
        private readonly TaskCompletionSource started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => started.Task;

        public TimeSpan? Interval { get; private set; }

        public Task RunAsync(
            TimeSpan interval,
            Func<CancellationToken, Task> onTick,
            CancellationToken cancellationToken)
        {
            Interval = interval;
            started.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
