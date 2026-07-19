extern alias DashboardApp;
using DashboardApp::MissionControl.Dashboard.Archive;
using DashboardApp::MissionControl.Dashboard.Configuration;
using DashboardApp::MissionControl.Dashboard.Refresh;
using MissionControl.Client.Agent;
using MissionControl.Contracts.Agent;
using Xunit;

namespace MissionControl.Tests;

public sealed class DashboardRefreshTests
{
    [Fact]
    public void RefreshOptionsProvideOperationalDefaults()
    {
        var options = new DashboardRefreshOptions();

        Assert.Equal(30, options.AgentSnapshotRefreshSeconds);
        Assert.Equal(30, options.EventRefreshSeconds);
        Assert.Equal(120, options.SnapshotStaleAfterSeconds);
    }

    [Fact]
    public void RefreshOptionsRejectNonOperationalIntervals()
    {
        var validator = new DashboardRefreshOptionsValidator();

        Assert.False(
            validator.Validate(
                null,
                new DashboardRefreshOptions
                {
                    AgentSnapshotRefreshSeconds = 0
                }).Succeeded);
        Assert.False(
            validator.Validate(
                null,
                new DashboardRefreshOptions
                {
                    EventRefreshSeconds = 0
                }).Succeeded);
        Assert.False(
            validator.Validate(
                null,
                new DashboardRefreshOptions
                {
                    SnapshotStaleAfterSeconds = 0
                }).Succeeded);
    }

    [Fact]
    public void SnapshotFreshnessUsesCapturedAtInsteadOfFrozenApiValues()
    {
        DateTimeOffset capturedAt =
            new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
        AgentSnapshotItem apiSnapshot =
            CreateSnapshot(
                capturedAt,
                cpuPercent: 37.5) with
            {
                AgeSeconds = 999,
                Stale = true
            };

        AgentSnapshotItem initial = SnapshotFreshness.Apply(
            apiSnapshot,
            capturedAt.AddSeconds(30),
            TimeSpan.FromSeconds(120));
        AgentSnapshotItem advanced = SnapshotFreshness.Apply(
            apiSnapshot,
            capturedAt.AddSeconds(121),
            TimeSpan.FromSeconds(120));
        AgentSnapshotItem future = SnapshotFreshness.Apply(
            apiSnapshot,
            capturedAt.AddSeconds(-10),
            TimeSpan.FromSeconds(120));

        Assert.Equal(30, initial.AgeSeconds);
        Assert.False(initial.Stale);
        Assert.Equal(121, advanced.AgeSeconds);
        Assert.True(advanced.Stale);
        Assert.Equal(0, future.AgeSeconds);
        Assert.False(future.Stale);
    }

    [Fact]
    public async Task OverviewAndServicesPollingUpdatesOperationalValuesAndLocalFreshness()
    {
        DateTimeOffset now =
            new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(now);
        var client = new QueueAgentSnapshotClient(
        [
            CreateSnapshot(now, 20),
            CreateSnapshot(
                now.AddSeconds(30),
                45,
                dockerAvailable: false,
                containerState: "exited",
                protocolSucceeded: false,
                publishSucceeded: false,
                memoryAvailableBytes: 4_000_000_000,
                containerImage: "missioncontrol/api:2",
                protocolEndpoint: "api.internal:17",
                protocolError: "Connection refused")
        ]);
        var controller = new AgentSnapshotRefreshController(
            client,
            timeProvider,
            TimeSpan.FromSeconds(120));

        Assert.True(controller.IsInitialLoading);
        Assert.True(
            await controller.RefreshAsync(CancellationToken.None));
        Assert.False(controller.IsInitialLoading);
        Assert.Equal(20, controller.CurrentSnapshot?.Host?.CpuPercent);

        var pollingLoop = new DashboardPollingLoop(timeProvider);
        using var cancellationSource = new CancellationTokenSource();
        var refreshed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task polling = pollingLoop.RunAsync(
            TimeSpan.FromSeconds(30),
            async token =>
            {
                await controller.RefreshAsync(token);
                refreshed.TrySetResult();
            },
            cancellationSource.Token);

        timeProvider.Advance(TimeSpan.FromSeconds(30));
        timeProvider.Tick();
        await refreshed.Task;

        AgentSnapshotItem current =
            Assert.IsType<AgentSnapshotItem>(controller.CurrentSnapshot);
        Assert.Equal(45, current.Host?.CpuPercent);
        Assert.Equal(
            4_000_000_000,
            current.Host?.MemoryAvailableBytes);
        Assert.False(current.DockerAvailable);
        Assert.Equal(
            "exited",
            Assert.Single(current.Containers).State);
        Assert.False(Assert.Single(current.Protocols).Succeeded);
        Assert.Equal(
            "api.internal:17",
            Assert.Single(current.Protocols).Endpoint);
        Assert.Equal(
            "Connection refused",
            Assert.Single(current.Protocols).Error);
        Assert.Equal(
            "missioncontrol/api:2",
            Assert.Single(current.Containers).Image);
        Assert.False(current.MissionControlPublishSucceeded);
        Assert.Equal(0, current.AgeSeconds);
        Assert.False(current.Stale);

        timeProvider.Advance(TimeSpan.FromSeconds(121));
        Assert.Equal(121, controller.CurrentSnapshot?.AgeSeconds);
        Assert.True(controller.CurrentSnapshot?.Stale);

        cancellationSource.Cancel();
        await polling;
    }

    [Fact]
    public async Task AgentRefreshFailureRetainsDataAndLaterSuccessClearsWarning()
    {
        DateTimeOffset now =
            new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new ManualTimeProvider(now);
        var client = new QueueAgentSnapshotClient(
        [
            CreateSnapshot(now, 20),
            new HttpRequestException("offline"),
            CreateSnapshot(now.AddMinutes(3), 55)
        ]);
        var controller = new AgentSnapshotRefreshController(
            client,
            timeProvider,
            TimeSpan.FromSeconds(120));

        await controller.RefreshAsync(CancellationToken.None);
        Assert.False(controller.IsInitialLoading);

        timeProvider.Advance(TimeSpan.FromSeconds(121));
        await controller.RefreshAsync(CancellationToken.None);

        Assert.Equal(20, controller.CurrentSnapshot?.Host?.CpuPercent);
        Assert.True(controller.CurrentSnapshot?.Stale);
        Assert.Contains("offline", controller.RefreshWarning);

        timeProvider.Advance(TimeSpan.FromSeconds(59));
        await controller.RefreshAsync(CancellationToken.None);

        Assert.Equal(55, controller.CurrentSnapshot?.Host?.CpuPercent);
        Assert.Null(controller.RefreshWarning);
        Assert.False(controller.CurrentSnapshot?.Stale);
    }

    [Fact]
    public async Task AgentManualAndAutomaticRefreshCannotOverlap()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var client =
            new BlockingAgentSnapshotClient(
                CreateSnapshot(now, 25));
        var controller = new AgentSnapshotRefreshController(
            client,
            new ManualTimeProvider(now),
            TimeSpan.FromMinutes(2));

        Task<bool> running =
            controller.RefreshAsync(CancellationToken.None);
        await client.Started;

        Assert.False(
            await controller.RefreshAsync(CancellationToken.None));
        Assert.Equal(1, client.CallCount);

        client.Release();
        Assert.True(await running);
    }

    [Fact]
    public async Task AgentDisposalCancellationDoesNotCreateWarning()
    {
        var client = new CancelledAgentSnapshotClient();
        var controller = new AgentSnapshotRefreshController(
            client,
            new ManualTimeProvider(DateTimeOffset.UtcNow),
            TimeSpan.FromMinutes(2));
        using var cancellationSource =
            new CancellationTokenSource();
        Task<bool> refresh =
            controller.RefreshAsync(cancellationSource.Token);
        await client.Started;

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => refresh);
        Assert.Null(controller.RefreshWarning);
    }

    [Fact]
    public async Task PollingLoopRunsOnControlledTickAndStopsAfterCancellation()
    {
        var timeProvider =
            new ManualTimeProvider(DateTimeOffset.UtcNow);
        var pollingLoop = new DashboardPollingLoop(timeProvider);
        using var cancellationSource =
            new CancellationTokenSource();
        int refreshCount = 0;
        var refreshed =
            new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        Task polling = pollingLoop.RunAsync(
            TimeSpan.FromSeconds(30),
            _ =>
            {
                refreshCount++;
                refreshed.TrySetResult();
                return Task.CompletedTask;
            },
            cancellationSource.Token);

        Assert.Equal(0, refreshCount);
        timeProvider.Tick();
        await refreshed.Task;
        Assert.Equal(1, refreshCount);

        cancellationSource.Cancel();
        await polling;
        timeProvider.Tick();
        Assert.Equal(1, refreshCount);
    }

    [Fact]
    public async Task EventsFirstPageRefreshesWithoutChangingActiveFilters()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ArchiveEventSummaryItem oldEvent =
            CreateEvent(1, now.AddMinutes(-1));
        ArchiveEventSummaryItem newEvent =
            CreateEvent(2, now);
        var client = new QueueArchiveEventClient(
        [
            new[] { oldEvent },
            new[] { oldEvent },
            new[] { newEvent, oldEvent }
        ]);
        var controller = new EventFeedController(client);

        await controller.LoadInitialAsync(CancellationToken.None);
        controller.SourceFilter = " api ";
        controller.EventTypeFilter = "created";
        await controller.ApplyFiltersAsync(CancellationToken.None);
        await controller.PollAsync(CancellationToken.None);

        Assert.Equal(" api ", controller.SourceFilter);
        Assert.Equal("created", controller.EventTypeFilter);
        Assert.Equal(2, controller.Events.Count);
        Assert.Equal(newEvent.EventId, controller.Events[0].EventId);
        Assert.False(controller.HasMore);
        Assert.False(controller.NewEventsAvailable);
        Assert.Equal("api", client.Calls[^1].Source);
        Assert.Equal("created", client.Calls[^1].EventType);
    }

    [Fact]
    public async Task EventsEmptyFilterCanBecomePopulatedOnPoll()
    {
        ArchiveEventSummaryItem newEvent =
            CreateEvent(1, DateTimeOffset.UtcNow);
        var client = new QueueArchiveEventClient(
        [
            Array.Empty<ArchiveEventSummaryItem>(),
            new[] { newEvent }
        ]);
        var controller = new EventFeedController(client)
        {
            SourceFilter = "api"
        };

        await controller.ApplyFiltersAsync(CancellationToken.None);
        Assert.Empty(controller.Events);

        await controller.PollAsync(CancellationToken.None);

        Assert.Equal(newEvent, Assert.Single(controller.Events));
    }

    [Fact]
    public async Task EventsOlderViewShowsIndicatorAndPreservesSelectionUntilDeliberateLoad()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ArchiveEventSummaryItem[] firstPage =
            Enumerable.Range(1, 51)
                .Select(index =>
                    CreateEvent(index, now.AddMinutes(-index)))
                .ToArray();
        ArchiveEventSummaryItem older =
            CreateEvent(100, now.AddHours(-2));
        ArchiveEventSummaryItem newest =
            CreateEvent(200, now.AddMinutes(1));
        var client = new QueueArchiveEventClient(
        [
            firstPage,
            new[] { older },
            new[] { newest }.Concat(firstPage.Take(50)).ToArray(),
            new[] { newest }.Concat(firstPage.Take(49)).ToArray()
        ]);
        var controller = new EventFeedController(client);

        await controller.LoadInitialAsync(CancellationToken.None);
        Assert.True(controller.HasMore);

        Guid selected = controller.Events[10].EventId;
        controller.SelectEvent(selected);
        await controller.LoadOlderAsync(CancellationToken.None);
        ArchiveEventSummaryItem[] visibleBeforePoll =
            controller.Events.ToArray();

        await controller.PollAsync(CancellationToken.None);

        Assert.Equal(visibleBeforePoll, controller.Events);
        Assert.True(controller.HasOlderEventsLoaded);
        Assert.True(controller.NewEventsAvailable);
        Assert.Equal(selected, controller.SelectedEventId);

        await controller.LoadNewestAsync(CancellationToken.None);

        Assert.Equal(newest.EventId, controller.Events[0].EventId);
        Assert.False(controller.HasOlderEventsLoaded);
        Assert.False(controller.NewEventsAvailable);
        Assert.Equal(selected, controller.SelectedEventId);
    }

    [Fact]
    public async Task EventsRefreshFailureRetainsListAndRecoveryClearsWarning()
    {
        ArchiveEventSummaryItem oldEvent =
            CreateEvent(1, DateTimeOffset.UtcNow.AddMinutes(-1));
        ArchiveEventSummaryItem newEvent =
            CreateEvent(2, DateTimeOffset.UtcNow);
        var client = new QueueArchiveEventClient(
        [
            new[] { oldEvent },
            new HttpRequestException("offline"),
            new[] { newEvent, oldEvent }
        ]);
        var controller = new EventFeedController(client)
        {
            SourceFilter = "api",
            EventTypeFilter = "created"
        };

        await controller.LoadInitialAsync(CancellationToken.None);
        bool hasMoreBeforeFailure = controller.HasMore;
        await controller.PollAsync(CancellationToken.None);

        Assert.Equal(oldEvent, Assert.Single(controller.Events));
        Assert.Equal(hasMoreBeforeFailure, controller.HasMore);
        Assert.Contains("offline", controller.RefreshWarning);
        Assert.Equal("api", controller.SourceFilter);
        Assert.Equal("created", controller.EventTypeFilter);

        await controller.PollAsync(CancellationToken.None);

        Assert.Equal(newEvent.EventId, controller.Events[0].EventId);
        Assert.Null(controller.RefreshWarning);
    }

    [Fact]
    public async Task EventsManualAndAutomaticRefreshCannotOverlap()
    {
        var client = new BlockingArchiveEventClient();
        var controller = new EventFeedController(client);

        Task<bool> running =
            controller.PollAsync(CancellationToken.None);
        await client.Started;

        Assert.False(
            await controller.ApplyFiltersAsync(CancellationToken.None));
        Assert.Equal(1, client.CallCount);

        client.Release([]);
        Assert.True(await running);
    }

    [Fact]
    public async Task EventsDisposalCancellationDoesNotCreateWarning()
    {
        var client = new BlockingArchiveEventClient();
        var controller = new EventFeedController(client);
        using var cancellationSource =
            new CancellationTokenSource();
        Task<bool> polling =
            controller.PollAsync(cancellationSource.Token);
        await client.Started;

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => polling);
        Assert.Null(controller.RefreshWarning);
    }

    private static AgentSnapshotItem CreateSnapshot(
        DateTimeOffset capturedAt,
        double cpuPercent,
        bool? dockerAvailable = true,
        string containerState = "running",
        bool protocolSucceeded = true,
        bool? publishSucceeded = true,
        long memoryAvailableBytes = 6_442_450_944,
        string? containerImage = "missioncontrol/api:1",
        string? protocolEndpoint = "api.internal:7",
        string? protocolError = null)
    {
        return new AgentSnapshotItem(
            Node: "node-1",
            CapturedAt: capturedAt,
            AgeSeconds: 0,
            Stale: false,
            Host: new HostMetricItem(
                8,
                cpuPercent,
                17_179_869_184,
                memoryAvailableBytes),
            MissionControlPublishSucceeded: publishSucceeded,
            LastMissionControlPublishAttemptAt:
                capturedAt.AddSeconds(-5),
            Protocols:
            [
                new PublicProtocolStatus(
                    "echo",
                    protocolSucceeded,
                    12,
                    protocolEndpoint,
                    protocolError)
            ],
            Containers:
            [
                new PublicContainerStatus(
                    "api",
                    containerState,
                    1_000_000,
                    2_000_000,
                    50,
                    10,
                    2,
                    containerImage)
            ],
            DockerAvailable: dockerAvailable,
            DockerError: dockerAvailable == false
                ? "Docker unavailable."
                : null);
    }

    private static ArchiveEventSummaryItem CreateEvent(
        int id,
        DateTimeOffset occurredAt)
    {
        return new ArchiveEventSummaryItem(
            EventId: CreateGuid(id),
            EventType: "created",
            Source: "api",
            SchemaVersion: 1,
            OccurredAt: occurredAt,
            ReceivedAt: occurredAt.AddSeconds(1),
            CorrelationId: null,
            CausationId: null);
    }

    private static Guid CreateGuid(int value)
    {
        byte[] bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        return new Guid(bytes);
    }

    private sealed class QueueAgentSnapshotClient(
        IEnumerable<object> responses) :
        IAgentSnapshotClient
    {
        private readonly Queue<object> responses = new(responses);

        public Task<AgentSnapshotItem> GetSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            object response = responses.Dequeue();

            return response is Exception exception
                ? Task.FromException<AgentSnapshotItem>(exception)
                : Task.FromResult((AgentSnapshotItem)response);
        }
    }

    private sealed class BlockingAgentSnapshotClient(
        AgentSnapshotItem response) :
        IAgentSnapshotClient
    {
        private readonly TaskCompletionSource started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => started.Task;

        public int CallCount { get; private set; }

        public async Task<AgentSnapshotItem> GetSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            started.TrySetResult();
            await released.Task.WaitAsync(cancellationToken);
            return response;
        }

        public void Release()
        {
            released.TrySetResult();
        }
    }

    private sealed class CancelledAgentSnapshotClient :
        IAgentSnapshotClient
    {
        private readonly TaskCompletionSource started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => started.Task;

        public async Task<AgentSnapshotItem> GetSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);
            throw new InvalidOperationException(
                "Cancellation was expected.");
        }
    }

    private sealed class QueueArchiveEventClient(
        IEnumerable<object> responses) :
        IArchiveEventClient
    {
        private readonly Queue<object> responses = new(responses);

        public List<ArchiveCall> Calls { get; } = [];

        public Task<IReadOnlyList<ArchiveEventSummaryItem>> GetRecentAsync(
            int limit = 50,
            string? source = null,
            string? eventType = null,
            ArchiveEventCursor? before = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new ArchiveCall(source, eventType, before));
            object response = responses.Dequeue();

            return response is Exception exception
                ? Task.FromException<IReadOnlyList<ArchiveEventSummaryItem>>(
                    exception)
                : Task.FromResult(
                    (IReadOnlyList<ArchiveEventSummaryItem>)
                    (ArchiveEventSummaryItem[])response);
        }

        public Task<ArchiveEventDetailsItem?> GetByIdAsync(
            Guid eventId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ArchiveEventDetailsItem?>(null);
        }

        public Task<ArchiveStatisticsItem> GetStatisticsAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class BlockingArchiveEventClient :
        IArchiveEventClient
    {
        private readonly TaskCompletionSource started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<IReadOnlyList<ArchiveEventSummaryItem>>
            released = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => started.Task;

        public int CallCount { get; private set; }

        public async Task<IReadOnlyList<ArchiveEventSummaryItem>> GetRecentAsync(
            int limit = 50,
            string? source = null,
            string? eventType = null,
            ArchiveEventCursor? before = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            started.TrySetResult();
            return await released.Task.WaitAsync(cancellationToken);
        }

        public Task<ArchiveEventDetailsItem?> GetByIdAsync(
            Guid eventId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ArchiveEventDetailsItem?>(null);
        }

        public Task<ArchiveStatisticsItem> GetStatisticsAsync(
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public void Release(
            IReadOnlyList<ArchiveEventSummaryItem> events)
        {
            released.TrySetResult(events);
        }
    }

    private sealed record ArchiveCall(
        string? Source,
        string? EventType,
        ArchiveEventCursor? Before);

    private sealed class ManualTimeProvider(
        DateTimeOffset utcNow) :
        TimeProvider
    {
        private readonly List<ManualTimer> timers = [];

        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ManualTimer(callback, state);
            timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan elapsed)
        {
            utcNow += elapsed;
        }

        public void Tick()
        {
            foreach (ManualTimer timer in timers.ToArray())
            {
                timer.Fire();
            }
        }

        private sealed class ManualTimer(
            TimerCallback callback,
            object? state) :
            ITimer
        {
            private bool disposed;

            public bool Change(
                TimeSpan dueTime,
                TimeSpan period)
            {
                return !disposed;
            }

            public void Dispose()
            {
                disposed = true;
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void Fire()
            {
                if (!disposed)
                {
                    callback(state);
                }
            }
        }
    }
}
