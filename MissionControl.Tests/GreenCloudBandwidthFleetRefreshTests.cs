extern alias DashboardApp;

using DashboardApp::MissionControl.Dashboard.GreenCloud;
using MissionControl.Client.Infrastructure;
using Xunit;

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
                client);

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

        public Task<
            IReadOnlyList<
                GreenCloudBandwidthNodeResult>>
            GetAllAsync(
                CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                responses.Dequeue());
        }
    }
}