
extern alias DashboardApp;

using DashboardApp::MissionControl.Dashboard.Agents;
using DashboardApp::MissionControl.Dashboard.Configuration;
using DashboardApp::MissionControl.Dashboard.Formatting;
using DashboardApp::MissionControl.Dashboard.Refresh;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MissionControl.Client.Archive;
using MissionControl.Client.WorkPlanning;
using MissionControl.Contracts.Archive;
using Xunit;

namespace MissionControl.Tests;

public sealed class ArchivePollingTests
{
    [Fact]
    public async Task DashboardPollingUpdatesArchiveCountAndCategoryBars()
    {
        var archive = new QueueArchiveClient(
        [
            Statistics(10, 6),
            Statistics(20, 15)
        ]);

        var polling = new CapturingPollingLoop();

        using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IArchiveEventClient>(archive)
            .AddSingleton<IAgentFleetClient>(new EmptyFleetClient())
            .AddSingleton<IDashboardDateTimeFormatter>(new Formatter())
            .AddSingleton(TimeProvider.System)
            .AddSingleton<IDashboardPollingLoop>(polling)
            .AddSingleton<IWorkPlanningAvailabilityClient>(new DisabledWork())
            .AddSingleton<IOptions<DashboardRefreshOptions>>(
                Options.Create(new DashboardRefreshOptions
                {
                    AgentSnapshotRefreshSeconds = 17,
                    EventRefreshSeconds = 43
                }))
            .BuildServiceProvider();

        await using var renderer = new HtmlRenderer(
            services,
            services.GetRequiredService<ILoggerFactory>());

        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var page = await renderer.RenderComponentAsync<
                DashboardApp::MissionControl.Dashboard.Components.Pages.Home>();


            string html = page.ToHtmlString();

            string pattern = @"Received in 24 hours[\s\S]*?<strong\b[^>]*>\s*10\s*</strong>";

            Assert.True(
                System.Text.RegularExpressions.Regex.IsMatch(html, pattern),
                $"24-hour counter markup:\n{html.Substring(
                    html.IndexOf("Received in 24 hours", StringComparison.Ordinal),
                    400)}");

            Assert.Contains("width: 60.00%", page.ToHtmlString());

            var archivePoll = Assert.Single(
                polling.Callbacks,
                item => item.Interval == TimeSpan.FromSeconds(43));

            await archivePoll.Callback(CancellationToken.None);

            Assert.Matches(
                @"Received in 24 hours[\s\S]*?<strong\b[^>]*>\s*20\s*</strong>",
                page.ToHtmlString());

            Assert.Contains("width: 75.00%", page.ToHtmlString());
            Assert.Equal(2, archive.CallCount);

            var pending = new TaskCompletionSource<ArchiveStatisticsItem>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            archive.BlockedResponse = pending;

            // Start a refresh that cannot complete yet.
            Task firstPoll = archivePoll.Callback(CancellationToken.None);

            await archive.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            // Another refresh arrives while the first is running.
            await archivePoll.Callback(CancellationToken.None);

            // Only one additional Archive request should have occurred.
            Assert.Equal(3, archive.CallCount);

            // Release the pending response.
            pending.SetResult(Statistics(30, 21));
            await firstPoll;

            // Confirm the completed request updates the UI.
            Assert.Contains("width: 70.00%", page.ToHtmlString());
            Assert.Equal(3, archive.CallCount);
        });
    }

    private static ArchiveStatisticsItem Statistics(long recent, long category) =>
        new(
            TotalEvents: 1000,
            EventsReceivedInWindow: recent,
            UniqueSources: 1,
            UniqueEventTypes: 1,
            LatestReceivedAt: null,
            TopSources: [new("test-source", category)],
            TopEventTypes: [new("test-event", category)]);

    private sealed class QueueArchiveClient(IEnumerable<ArchiveStatisticsItem> values)
        : IArchiveEventClient
    {
        private readonly Queue<ArchiveStatisticsItem> _values = new(values);

        public int CallCount { get; private set; }

        public TaskCompletionSource<ArchiveStatisticsItem>? BlockedResponse { get; set; }

        public TaskCompletionSource RequestStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ArchiveStatisticsItem> GetStatisticsAsync(
            CancellationToken cancellationToken = default)
        {
            CallCount++;

            if (BlockedResponse is { } pending)
            {
                BlockedResponse = null;
                RequestStarted.TrySetResult();
                return pending.Task.WaitAsync(cancellationToken);
            }

            return Task.FromResult(_values.Dequeue());
        }

        public Task<IReadOnlyList<ArchiveEventSummaryItem>> GetRecentAsync(
            int limit = 50,
            string? source = null,
            string? eventType = null,
            ArchiveEventCursor? before = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ArchiveEventDetailsItem?> GetByIdAsync(
            Guid eventId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class CapturingPollingLoop : IDashboardPollingLoop
    {
        public List<(TimeSpan Interval, Func<CancellationToken, Task> Callback)>
            Callbacks
        { get; } = [];

        public Task RunAsync(
            TimeSpan interval,
            Func<CancellationToken, Task> onTick,
            CancellationToken cancellationToken)
        {
            Callbacks.Add((interval, onTick));
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyFleetClient : IAgentFleetClient
    {
        public Task<IReadOnlyList<AgentNodeResult>> GetSnapshotsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentNodeResult>>([]);
    }

    private sealed class DisabledWork : IWorkPlanningAvailabilityClient
    {
        public Task<WorkPlanningAvailability> GetAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkPlanningAvailability(false));
    }

    private sealed class Formatter : IDashboardDateTimeFormatter
    {
        public string Format(DateTimeOffset value) => value.ToString("O");
        public string FormatRelative(DateTimeOffset value) => value.ToString("O");
    }
}
