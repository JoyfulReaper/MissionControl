extern alias DashboardApp;

using DashboardApp::MissionControl.Dashboard.Agent;
using DashboardApp::MissionControl.Dashboard.Components.Pages;
using DashboardApp::MissionControl.Dashboard.Configuration;
using DashboardApp::MissionControl.Dashboard.Refresh;
using DashboardApp::MissionControl.Dashboard.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MissionControl.Tests;

public sealed class DashboardServiceCatalogReloadTests
{
    [Fact]
    public async Task ValidReloadReplacesAddedRemovedAndUpdatedServices()
    {
        var monitor = new FakeServiceCatalogMonitor();
        using var changes = new SemaphoreSlim(0);
        await using var controller = new ServiceCatalogReloadController(
            CreateCatalog(
                CreateService("one", "Original"),
                CreateService("removed", "Removed")),
            monitor,
            new ServiceCatalogOptionsValidator(),
            NullLogger<ServiceCatalogReloadController>.Instance,
            update => update(),
            () => changes.Release());

        Assert.Equal(2, controller.Services.Count);
        Assert.Equal("Original", controller.Services[0].Name);

        monitor.Notify(
            CreateCatalog(
                CreateService("one", "Updated"),
                CreateService("added", "Added")));
        Assert.True(await changes.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Collection(
            controller.Services,
            service =>
            {
                Assert.Equal("one", service.Id);
                Assert.Equal("Updated", service.Name);
            },
            service =>
            {
                Assert.Equal("added", service.Id);
                Assert.Equal("Added", service.Name);
            });
        Assert.Null(controller.ReloadWarning);
    }

    [Fact]
    public async Task InvalidReloadRetainsCatalogAndLaterValidReloadRecovers()
    {
        var monitor = new FakeServiceCatalogMonitor();
        using var changes = new SemaphoreSlim(0);
        await using var controller = new ServiceCatalogReloadController(
            CreateCatalog(CreateService("one", "Original")),
            monitor,
            new ServiceCatalogOptionsValidator(),
            NullLogger<ServiceCatalogReloadController>.Instance,
            update => update(),
            () => changes.Release());

        monitor.Notify(new ServiceCatalogOptions());
        Assert.True(await changes.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal("Original", Assert.Single(controller.Services).Name);
        Assert.Equal(
            ServiceCatalogReloadController.ReloadWarningMessage,
            controller.ReloadWarning);

        monitor.Notify(
            CreateCatalog(CreateService("one", "Recovered")));
        Assert.True(await changes.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal("Recovered", Assert.Single(controller.Services).Name);
        Assert.Null(controller.ReloadWarning);
    }

    [Fact]
    public async Task BindingFailureUsesSafeWarningAndRetainsCatalog()
    {
        var monitor = new FakeServiceCatalogMonitor();
        using var changes = new SemaphoreSlim(0);
        await using var controller = new ServiceCatalogReloadController(
            CreateCatalog(CreateService("one", "Original")),
            monitor,
            new ServiceCatalogOptionsValidator(),
            NullLogger<ServiceCatalogReloadController>.Instance,
            update => update(),
            () => changes.Release());

        monitor.NotifyBindingFailure();
        Assert.True(await changes.WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal("Original", Assert.Single(controller.Services).Name);
        Assert.Equal(
            ServiceCatalogReloadController.ReloadWarningMessage,
            controller.ReloadWarning);
        Assert.DoesNotContain(
            Path.DirectorySeparatorChar,
            Assert.IsType<string>(controller.ReloadWarning));
    }

    [Fact]
    public async Task DisposalUnsubscribesAndPreventsLaterUpdates()
    {
        var monitor = new FakeServiceCatalogMonitor();
        int stateChangeCount = 0;
        var controller = new ServiceCatalogReloadController(
            CreateCatalog(CreateService("one", "Original")),
            monitor,
            new ServiceCatalogOptionsValidator(),
            NullLogger<ServiceCatalogReloadController>.Instance,
            update => update(),
            () => stateChangeCount++);

        Assert.Equal(1, monitor.SubscriberCount);
        await controller.DisposeAsync();
        Assert.Equal(0, monitor.SubscriberCount);

        monitor.Notify(
            CreateCatalog(CreateService("one", "Ignored")));

        Assert.Equal("Original", Assert.Single(controller.Services).Name);
        Assert.Equal(0, stateChangeCount);
    }

    [Fact]
    public async Task ConfigurationReloadTokenProducesBoundCandidate()
    {
        IConfigurationRoot configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    CreateConfigurationValues("Initial"))
                .Build();
        using var monitor = new ConfigurationServiceCatalogMonitor(
            configuration,
            NullLogger<ConfigurationServiceCatalogMonitor>.Instance);
        var changed = new TaskCompletionSource<
            ServiceCatalogReloadCandidate>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        using IDisposable subscription = monitor.OnChange(
            candidate => changed.TrySetResult(candidate));

        configuration["ServiceCatalog:Services:0:Name"] = "Reloaded";
        configuration.Reload();
        ServiceCatalogReloadCandidate candidate =
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(candidate.BindingSucceeded);
        Assert.Equal(
            "Reloaded",
            Assert.Single(
                Assert.IsType<ServiceCatalogOptions>(
                    candidate.Options).Services).Name);
    }

    [Fact]
    public void ValidatorRejectsUnsafeCatalogShapes()
    {
        var validator = new ServiceCatalogOptionsValidator();
        ServiceDefinition duplicateOne =
            CreateService("duplicate", "One");
        ServiceDefinition duplicateTwo =
            CreateService("DUPLICATE", "Two");
        duplicateTwo.ContainerName = duplicateOne.ContainerName;
        duplicateTwo.ProtocolServiceKey =
            duplicateOne.ProtocolServiceKey;
        duplicateTwo.ApplicationUrl = "relative/path";
        duplicateTwo.SearchTerms = [" "];

        ValidateOptionsResult result = validator.Validate(
            null,
            CreateCatalog(duplicateOne, duplicateTwo));

        Assert.True(result.Failed);
        Assert.Contains(
            "Dashboard service IDs must be unique.",
            result.Failures);
        Assert.Contains(
            "Dashboard service container names must be unique when configured.",
            result.Failures);
        Assert.Contains(
            "Dashboard protocol service keys must be unique when configured.",
            result.Failures);
        Assert.Contains(
            "Dashboard service search terms must not contain blank entries.",
            result.Failures);
        Assert.Contains(
            "Dashboard service URLs must be absolute HTTP or HTTPS URLs.",
            result.Failures);
    }

    [Fact]
    public void InitialInvalidCatalogFailsOptionsResolution()
    {
        var services = new ServiceCollection();
        services
            .AddOptions<ServiceCatalogOptions>()
            .Configure(options => options.Services = []);
        services.AddSingleton<
            IValidateOptions<ServiceCatalogOptions>,
            ServiceCatalogOptionsValidator>();
        using ServiceProvider provider =
            services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(
            () => provider
                .GetRequiredService<IOptions<ServiceCatalogOptions>>()
                .Value);
    }

    [Fact]
    public async Task PageReloadPreservesSnapshotFilterAndSinglePollingLoop()
    {
        DateTimeOffset capturedAt = DateTimeOffset.UtcNow;
        AgentSnapshotItem snapshot = CreateSnapshot(capturedAt);
        var agentClient = new ReloadTestAgentClient(snapshot);
        var pollingLoop = new ControlledPollingLoop();
        var monitor = new FakeServiceCatalogMonitor();
        var page = new TestServicesPage
        {
            AgentClient = agentClient,
            RefreshOptions = Options.Create(
                new DashboardRefreshOptions()),
            TimeProvider = TimeProvider.System,
            PollingLoop = pollingLoop,
            CatalogOptions = Options.Create(
                CreateCatalog(CreateService("one", "Original"))),
            CatalogMonitor = monitor,
            CatalogValidator = new ServiceCatalogOptionsValidator(),
            CatalogLogger =
                NullLogger<ServiceCatalogReloadController>.Instance,
            FilterForTesting = "api"
        };

        await page.InitializeAsync();
        Task inProgressRefresh = pollingLoop.TriggerAsync();
        await agentClient.SecondRequestStarted;

        monitor.Notify(
            CreateCatalog(CreateService("two", "Reloaded")));
        await page.CatalogChanged.Task.WaitAsync(
            TimeSpan.FromSeconds(5));

        Assert.Equal(1, pollingLoop.RunCount);
        Assert.Equal(2, agentClient.CallCount);
        Assert.False(inProgressRefresh.IsCompleted);
        Assert.False(agentClient.SecondRequestCancelled);
        Assert.Equal("api", page.FilterForTesting);
        Assert.Equal(
            capturedAt,
            Assert.IsType<AgentSnapshotItem>(
                page.SnapshotForTesting).CapturedAt);
        Assert.Equal("Reloaded", Assert.Single(page.CurrentCatalog).Name);

        agentClient.ReleaseSecondRequest();
        await inProgressRefresh;

        int renderCountBeforeDispose = page.CatalogRenderCount;
        await page.DisposeAsync();
        Assert.Equal(0, monitor.SubscriberCount);

        monitor.Notify(
            CreateCatalog(CreateService("three", "Ignored")));
        Assert.Equal(renderCountBeforeDispose, page.CatalogRenderCount);
        Assert.Equal("Reloaded", Assert.Single(page.CurrentCatalog).Name);
    }

    private static ServiceCatalogOptions CreateCatalog(
        params ServiceDefinition[] services)
    {
        return new ServiceCatalogOptions
        {
            Services = services.ToList()
        };
    }

    private static ServiceDefinition CreateService(
        string id,
        string name)
    {
        return new ServiceDefinition
        {
            Id = id,
            Name = name,
            Group = "Applications",
            Summary = $"{name} summary",
            Description = $"{name} description",
            ContainerName = $"{id}-container",
            Visibility = "Private",
            ProtocolServiceKey = $"{id}-probe",
            SearchTerms = [id, name]
        };
    }

    private static Dictionary<string, string?>
        CreateConfigurationValues(string name)
    {
        return new Dictionary<string, string?>
        {
            ["ServiceCatalog:Services:0:Id"] = "one",
            ["ServiceCatalog:Services:0:Name"] = name,
            ["ServiceCatalog:Services:0:Group"] = "Applications",
            ["ServiceCatalog:Services:0:Summary"] = "Summary",
            ["ServiceCatalog:Services:0:Description"] = "Description",
            ["ServiceCatalog:Services:0:Visibility"] = "Private"
        };
    }

    private static AgentSnapshotItem CreateSnapshot(
        DateTimeOffset capturedAt)
    {
        return new AgentSnapshotItem(
            Node: "node-1",
            CapturedAt: capturedAt,
            AgeSeconds: 0,
            Stale: false,
            Host: null,
            MissionControlPublishSucceeded: true,
            LastMissionControlPublishAttemptAt: capturedAt,
            Protocols: [],
            Containers: [],
            DockerAvailable: true,
            DockerError: null);
    }

    private sealed class FakeServiceCatalogMonitor :
        IServiceCatalogMonitor
    {
        private readonly List<Action<ServiceCatalogReloadCandidate>>
            _listeners = [];

        public int SubscriberCount => _listeners.Count;

        public IDisposable OnChange(
            Action<ServiceCatalogReloadCandidate> listener)
        {
            _listeners.Add(listener);
            return new CallbackDisposable(
                () => _listeners.Remove(listener));
        }

        public void Notify(ServiceCatalogOptions options)
        {
            Notify(new ServiceCatalogReloadCandidate(
                options,
                BindingSucceeded: true));
        }

        public void NotifyBindingFailure()
        {
            Notify(new ServiceCatalogReloadCandidate(
                Options: null,
                BindingSucceeded: false));
        }

        private void Notify(ServiceCatalogReloadCandidate candidate)
        {
            foreach (Action<ServiceCatalogReloadCandidate> listener in
                     _listeners.ToArray())
            {
                listener(candidate);
            }
        }
    }

    private sealed class CallbackDisposable(Action callback) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            callback();
        }
    }

    private sealed class TestServicesPage : Services
    {
        public TaskCompletionSource CatalogChanged { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CatalogRenderCount { get; private set; }

        public Task InitializeAsync()
        {
            return OnInitializedAsync();
        }

        protected override Task DispatchCatalogUpdateAsync(
            Func<Task> update)
        {
            return update();
        }

        protected override void NotifyCatalogStateChanged()
        {
            CatalogRenderCount++;
            CatalogChanged.TrySetResult();
        }

        protected override Task DispatchComponentStateChangeAsync()
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ControlledPollingLoop : IDashboardPollingLoop
    {
        private Func<CancellationToken, Task>? _onTick;
        private CancellationToken _cancellationToken;

        public int RunCount { get; private set; }

        public async Task RunAsync(
            TimeSpan interval,
            Func<CancellationToken, Task> onTick,
            CancellationToken cancellationToken)
        {
            RunCount++;
            _onTick = onTick;
            _cancellationToken = cancellationToken;

            try
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        public Task TriggerAsync()
        {
            return Assert.IsType<Func<CancellationToken, Task>>(
                _onTick)(_cancellationToken);
        }
    }

    private sealed class ReloadTestAgentClient(
        AgentSnapshotItem snapshot) : IAgentSnapshotClient
    {
        private readonly TaskCompletionSource _secondRequestStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseSecondRequest =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SecondRequestStarted => _secondRequestStarted.Task;

        public int CallCount { get; private set; }

        public bool SecondRequestCancelled { get; private set; }

        public async Task<AgentSnapshotItem> GetSnapshotAsync(
            CancellationToken cancellationToken = default)
        {
            CallCount++;

            if (CallCount == 1)
            {
                return snapshot;
            }

            _secondRequestStarted.TrySetResult();

            try
            {
                await _releaseSecondRequest.Task.WaitAsync(
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                SecondRequestCancelled = true;
                throw;
            }

            return snapshot with
            {
                CapturedAt = snapshot.CapturedAt.AddMinutes(1)
            };
        }

        public void ReleaseSecondRequest()
        {
            _releaseSecondRequest.TrySetResult();
        }
    }
}
