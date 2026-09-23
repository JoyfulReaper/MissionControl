extern alias DashboardApp;

using DashboardApp::MissionControl.Dashboard.GreenCloud;
using Xunit;

namespace MissionControl.Tests;

public sealed class GreenCloudBandwidthRateStateTests
{
    [Fact]
    public void TracksServersIndependently()
    {
        var state =
            new GreenCloudBandwidthRateState();

        DateTimeOffset start =
            new(
                2026,
                9,
                23,
                12,
                0,
                0,
                TimeSpan.Zero);

        var clankerInitial =
            state.Update(
                "clanker",
                rx: 1_000,
                tx: 2_000,
                start);

        var scopeCreepInitial =
            state.Update(
                "scopecreep",
                rx: 10_000,
                tx: 20_000,
                start);

        Assert.Null(
            clankerInitial.RxBytesPerSecond);

        Assert.Null(
            scopeCreepInitial.RxBytesPerSecond);

        var clanker =
            state.Update(
                "clanker",
                rx: 1_600,
                tx: 2_400,
                start.AddSeconds(10));

        var scopeCreep =
            state.Update(
                "scopecreep",
                rx: 11_000,
                tx: 23_000,
                start.AddSeconds(10));

        Assert.Equal(
            60,
            clanker.RxBytesPerSecond);

        Assert.Equal(
            40,
            clanker.TxBytesPerSecond);

        Assert.Equal(
            100,
            scopeCreep.RxBytesPerSecond);

        Assert.Equal(
            300,
            scopeCreep.TxBytesPerSecond);
    }
}