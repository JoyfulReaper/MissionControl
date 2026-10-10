extern alias AgentApp;

using AgentApp::MissionControl.Agent;
using AgentApp::MissionControl.Agent.Endpoints;
using AgentApp::MissionControl.Agent.Host;
using AgentApp::MissionControl.Agent.Models;
using AgentApp::MissionControl.Agent.Storage;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MissionControl.Contracts.Agent;
using MissionControl.UI.Components.Overview;
using System.Text.Json;
using Xunit;

namespace MissionControl.Tests;

public sealed class DiskUsageTests
{
    [Fact]
    public void CalculatesUsedFreeAvailableAndPercentIncludingReservedSpace()
    {
        PublicDiskUsage disk = DiskUsageCollector.Calculate("/", "ext4", new DiskSpace(1000, 400, 350, "ext4"));
        Assert.Equal(600, disk.UsedBytes);
        Assert.Equal(400, disk.FreeBytes);
        Assert.Equal(350, disk.AvailableBytes);
        Assert.Equal(60, disk.UsagePercent);
        PublicDiskUsage empty = DiskUsageCollector.Calculate("/", "ext4", new DiskSpace(0, -1, 20, "ext4"));
        Assert.Equal(0, empty.UsagePercent);
        Assert.Equal(0, empty.FreeBytes);
        Assert.Equal(0, empty.AvailableBytes);
    }

    [Theory]
    [InlineData("proc")]
    [InlineData("sysfs")]
    [InlineData("tmpfs")]
    [InlineData("devtmpfs")]
    [InlineData("cgroup")]
    [InlineData("cgroup2")]
    [InlineData("overlay")]
    [InlineData("squashfs")]
    [InlineData("nfs")]
    [InlineData("fusectl")]
    public void FiltersPseudoEphemeralReadOnlyImageAndNetworkFilesystems(string format)
    {
        Assert.False(DiskUsageCollector.IsCapacityMount(new("0:1", "/", "/test", format)));
    }

    [Fact]
    public async Task CollectsDistinctLocalFilesystemsAndUnescapesMountNames()
    {
        var source = new Source
        {
            Mounts = Line("8:1", "/", "ext4") + Line("8:1", "/duplicate", "ext4")
                + Line("8:2", "/data\\040disk", "xfs") + Line("0:2", "/proc", "proc")
                + Line("8:1", "/etc/hosts", "ext4", "/container/hosts")
                + Line("8:3", "/btrfs", "btrfs", "/@")
        };
        DiskCollectionResult result = await Collector(source).CollectAsync(default);
        Assert.Null(result.Error);
        Assert.Equal(new[] { "/", "/btrfs", "/data disk" }, result.Disks!.Select(disk => disk.MountPoint));
        Assert.Equal(new[] { "/", "/btrfs", "/data disk" }, source.Paths);
    }

    [Fact]
    public async Task PartialDiskFailureKeepsOtherDisksAndHostMetrics()
    {
        var source = new Source { Mounts = Line("8:1", "/", "ext4") + Line("8:2", "/data", "xfs"), FailPath = "/data" };
        var hostCollector = new HostMetricsCollector(Collector(source));
        var original = new HostMetric(4, 12, 1000, 400) { LoadAverage1Minute = 1 };
        HostMetric result = await hostCollector.AttachDiskUsageAsync(original, default);
        Assert.Equal(original.CpuPercent, result.CpuPercent);
        Assert.Equal(original.MemoryAvailableBytes, result.MemoryAvailableBytes);
        Assert.Equal(original.LoadAverage1Minute, result.LoadAverage1Minute);
        Assert.Equal("/", Assert.Single(result.Disks!).MountPoint);
        Assert.Equal(DiskUsageCollector.ReadError, result.DiskError);
        Assert.DoesNotContain("private", result.DiskError);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnreadableOrMalformedMountTableDoesNotDiscardHost(bool unreadable)
    {
        var source = new Source { Mounts = "malformed", FailRead = unreadable };
        HostMetric result = await new HostMetricsCollector(Collector(source)).AttachDiskUsageAsync(new HostMetric(4, 12, 1000, 400), default);
        Assert.Equal(12, result.CpuPercent);
        Assert.Empty(result.Disks!);
        Assert.Equal(DiskUsageCollector.ReadError, result.DiskError);
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new HostMetricsCollector(Collector(new Source())).AttachDiskUsageAsync(new HostMetric(4, 12, 1000, 400), cancellation.Token));
    }

    [Fact]
    public async Task ContainerWithoutHostVisibilityNeverReadsContainerDisk()
    {
        var source = new Source { IsContainer = true };
        DiskCollectionResult result = await Collector(source).CollectAsync(default);
        Assert.Empty(result.Disks!);
        Assert.Contains("host root bind", result.Error);
        Assert.Equal(0, source.Reads);
        Assert.Empty(source.Paths);
    }

    [Fact]
    public async Task OverlayRootWithoutContainerMarkerDoesNotReportBindMountedContainerFiles()
    {
        var source = new Source { Mounts = Line("0:1", "/", "overlay") + Line("8:1", "/data", "ext4") };
        DiskCollectionResult result = await Collector(source).CollectAsync(default);
        Assert.Empty(result.Disks!);
        Assert.NotNull(result.Error);
        Assert.Empty(source.Paths);
    }

    [Fact]
    public async Task ContainerUsesExplicitHostTableAndRootAndRejectsOverlayVisibility()
    {
        string root = Path.Combine(Path.GetTempPath(), "disk-host-root");
        var source = new Source
        {
            IsContainer = true,
            Mounts = Line("8:1", "/data", "ext4"),
            VisibleMounts = Line("8:1", root, "ext4")
        };
        var options = new DiskOptions { HostRootPath = root, MountInfoPath = "/run/host-mountinfo" };
        DiskCollectionResult result = await Collector(source, options).CollectAsync(default);
        Assert.Contains("/run/host-mountinfo", source.ReadPaths);
        Assert.Equal(Path.Combine(root, "data"), Assert.Single(source.Paths));
        Assert.Equal("/data", Assert.Single(result.Disks!).MountPoint);
        source.VisibleFormat = "overlay";
        DiskCollectionResult unavailable = await Collector(source, options).CollectAsync(default);
        Assert.Empty(unavailable.Disks!);
        Assert.NotNull(unavailable.Error);
        DiskCollectionResult missingTable = await Collector(source, new DiskOptions { HostRootPath = root }).CollectAsync(default);
        Assert.NotNull(missingTable.Error);
    }

    [Fact]
    public async Task MissingHostSubmountCannotReportParentFilesystemCapacity()
    {
        string root = Path.Combine(Path.GetTempPath(), "disk-host-root");
        var source = new Source
        {
            IsContainer = true,
            Mounts = Line("8:1", "/", "ext4") + Line("8:2", "/data", "ext4"),
            VisibleMounts = Line("8:1", root, "ext4")
        };
        DiskCollectionResult result = await Collector(source, new DiskOptions
        {
            HostRootPath = root,
            MountInfoPath = "/run/host-mountinfo"
        }).CollectAsync(default);
        Assert.Equal("/", Assert.Single(result.Disks!).MountPoint);
        Assert.NotNull(result.Error);
        Assert.Single(source.Paths);
    }

    [Fact]
    public async Task UnsupportedPlatformsLeaveDiskDataAbsent()
    {
        var source = new Source { IsLinux = false };
        DiskCollectionResult result = await Collector(source).CollectAsync(default);
        Assert.Null(result.Disks);
        Assert.Null(result.Error);
        Assert.Equal(0, source.Reads);
    }

    [Fact]
    public async Task DisksPersistAndProjectWithOldSnapshotsRemainingReadable()
    {
        await using var fixture = await AgentSnapshotStoreFixture.CreateAsync();
        var snapshot = new NodeSnapshotEvent("node", DateTimeOffset.UtcNow,
            new HostMetric(4, 12, 1000, 400) { Disks = [ExampleDisk], DiskError = "private details" }, [], []);
        await fixture.SnapshotStore.SaveAsync(snapshot, default);
        StoredNodeSnapshot stored = (await fixture.SnapshotStore.GetAsync("node", default))!;
        PublicNodeSnapshot projected = AgentSnapshotEndpointRouteBuilderExtensions.CreatePublicSnapshot(stored, snapshot.CapturedAt, TimeSpan.FromMinutes(2));
        Assert.Equal(ExampleDisk, Assert.Single(projected.Host!.Disks!));
        Assert.DoesNotContain("private", projected.Host.DiskError);
        PublicNodeSnapshot roundTrip = JsonSerializer.Deserialize<PublicNodeSnapshot>(JsonSerializer.Serialize(projected))!;
        Assert.Equal(ExampleDisk, Assert.Single(roundTrip.Host!.Disks!));
        string eventJson = JsonSerializer.Serialize(snapshot, AgentApp::MissionControl.Agent.Publishing.AgentEventJsonContext.Default.NodeSnapshotEvent);
        Assert.Equal(ExampleDisk, Assert.Single(JsonSerializer.Deserialize<NodeSnapshotEvent>(eventJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Host!.Disks!));
        PublicHostMetric old = JsonSerializer.Deserialize<PublicHostMetric>("{\"LogicalProcessorCount\":4,\"CpuPercent\":12}")!;
        Assert.Null(old.Disks);
        Assert.Null(old.DiskError);
        HostMetric oldStored = JsonSerializer.Deserialize<HostMetric>("{\"LogicalProcessorCount\":4}")!;
        Assert.Null(oldStored.Disks);
        PublicNodeSnapshot fresh = AgentSnapshotEndpointRouteBuilderExtensions.CreatePublicSnapshot(stored, snapshot.CapturedAt,
            TimeSpan.FromMinutes(2), new HostMetric(4, 15, 1000, 400) { Disks = [ExampleDisk with { UsedBytes = 100 }] });
        Assert.Equal(100, Assert.Single(fresh.Host!.Disks!).UsedBytes);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task SharedDashboardAndMobileResourceCardsHandleDisksAndOlderHosts(bool hasDisks, bool diskFailure)
    {
        using ServiceProvider provider = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        string markup = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<NodeResourceCards>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(NodeResourceCards.Host)] = new PublicHostMetric(4, 12, 1000, 400)
                {
                    Disks = hasDisks ? [ExampleDisk] : null,
                    DiskError = diskFailure ? "private failure details" : null
                }
            }));
            return component.ToHtmlString();
        });
        Assert.Contains("Memory", markup);
        Assert.Contains("CPU", markup);
        Assert.Equal(hasDisks || diskFailure, markup.Contains("Disk usage"));
        Assert.DoesNotContain("private", markup);
        if (diskFailure) Assert.Contains("unavailable for one or more filesystems", markup);
        if (hasDisks)
        {
            Assert.Contains("/data", markup);
            Assert.Contains("60%", markup);
            Assert.Contains("6 GB / 10 GB used", markup);
        }
    }

    private static PublicDiskUsage ExampleDisk => new("/data", 10L << 30, 6L << 30, 4L << 30, 3L << 30, 60, "ext4");
    private static DiskUsageCollector Collector(Source source, DiskOptions? disks = null) => new(source,
        Options.Create(new AgentOptions { Disks = disks ?? new() }));
    private static string Line(string device, string mount, string format, string root = "/") =>
        $"1 0 {device} {root} {mount} rw - {format} /dev/test rw\n";

    private sealed class Source : IDiskMetricsSource
    {
        public bool IsLinux { get; init; } = true;
        public bool IsContainer { get; init; }
        public string Mounts { get; init; } = Line("8:1", "/", "ext4");
        public string? FailPath { get; init; }
        public bool FailRead { get; init; }
        public string VisibleFormat { get; set; } = "ext4";
        public string? VisibleMounts { get; init; }
        public List<string> Paths { get; } = [];
        public int Reads { get; private set; }
        public List<string> ReadPaths { get; } = [];
        public Task<string> ReadMountInfoAsync(string path, CancellationToken cancellationToken)
        {
            Reads++;
            ReadPaths.Add(path);
            return FailRead ? Task.FromException<string>(new IOException("private failure"))
                : Task.FromResult(path == "/proc/self/mountinfo" && VisibleMounts is not null ? VisibleMounts : Mounts);
        }
        public DiskSpace GetSpace(string path)
        {
            Paths.Add(path);
            if (path == FailPath) throw new IOException("private disk failure");
            string format = path == "/btrfs" ? "btrfs" : path == "/data disk" ? "xfs" : VisibleFormat;
            return new(1000, 400, 350, format);
        }
    }
}
