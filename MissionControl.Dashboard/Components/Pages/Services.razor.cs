using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using MissionControl.Contracts.Agent;
using MissionControl.Contracts.Services;
using MissionControl.Dashboard.Agent;
using MissionControl.Dashboard.Configuration;
using MissionControl.Dashboard.Refresh;
using MissionControl.Dashboard.Services;

namespace MissionControl.Dashboard.Components.Pages;

public partial class Services : IAsyncDisposable
{
    private string? _filter;
    private AgentSnapshotRefreshController _agentRefresh = null!;
    private ServiceCatalogReloadController? _catalogReload;
    private readonly CancellationTokenSource _disposeSource = new();
    private Task? _pollingTask;
    private bool _isManualRefresh;
    private bool _disposed;

    private AgentSnapshotItem? CurrentSnapshot =>
        _agentRefresh?.CurrentSnapshot;

    internal AgentSnapshotItem? SnapshotForTesting => CurrentSnapshot;

    internal string? FilterForTesting
    {
        get => _filter;
        set => _filter = value;
    }

    internal IReadOnlyList<ServiceDefinition> CurrentCatalog =>
        _catalogReload?.Services ?? CatalogOptions.Value.Services;

    internal string? CatalogReloadWarning =>
        _catalogReload?.ReloadWarning;

    [Inject]
    internal IAgentSnapshotClient AgentClient { get; set; } = null!;

    [Inject]
    internal IOptions<DashboardRefreshOptions> RefreshOptions { get; set; } =
        null!;

    [Inject]
    internal TimeProvider TimeProvider { get; set; } = null!;

    [Inject]
    internal IDashboardPollingLoop PollingLoop { get; set; } = null!;

    [Inject]
    internal IOptions<ServiceCatalogOptions> CatalogOptions { get; set; } =
        null!;

    [Inject]
    internal IServiceCatalogMonitor CatalogMonitor { get; set; } = null!;

    [Inject]
    internal IValidateOptions<ServiceCatalogOptions> CatalogValidator
    {
        get;
        set;
    } = null!;

    [Inject]
    internal ILogger<ServiceCatalogReloadController> CatalogLogger
    {
        get;
        set;
    } = null!;

    protected override async Task OnInitializedAsync()
    {
        _catalogReload = new ServiceCatalogReloadController(
            CatalogOptions.Value,
            CatalogMonitor,
            CatalogValidator,
            CatalogLogger,
            DispatchCatalogUpdateAsync,
            NotifyCatalogStateChanged);

        _agentRefresh = new AgentSnapshotRefreshController(
            AgentClient,
            TimeProvider,
            TimeSpan.FromSeconds(
                RefreshOptions.Value.SnapshotStaleAfterSeconds));

        await _agentRefresh.RefreshAsync(
            _disposeSource.Token);

        _pollingTask = PollingLoop.RunAsync(
            TimeSpan.FromSeconds(
                RefreshOptions.Value.AgentSnapshotRefreshSeconds),
            RefreshSnapshotFromPollingAsync,
            _disposeSource.Token);
    }

    private async Task RefreshSnapshotAsync()
    {
        if (_agentRefresh.IsRefreshing)
        {
            return;
        }

        _isManualRefresh = true;

        try
        {
            await _agentRefresh.RefreshAsync(
                _disposeSource.Token);
        }
        finally
        {
            _isManualRefresh = false;
        }
    }

    private async Task RefreshSnapshotFromPollingAsync(
        CancellationToken cancellationToken)
    {
        await _agentRefresh.RefreshAsync(cancellationToken);

        if (!_disposed && !cancellationToken.IsCancellationRequested)
        {
            await DispatchComponentStateChangeAsync();
        }
    }

    private ServicesView CreateView()
    {
        IReadOnlyList<ServiceDefinition> services =
            CurrentCatalog;

        Dictionary<string, PublicContainerStatus> containersByName =
            (CurrentSnapshot?.Containers ?? [])
                .GroupBy(
                    container => container.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);

        Dictionary<string, PublicProtocolStatus> protocolsByService =
            (CurrentSnapshot?.Protocols ?? [])
                .GroupBy(
                    protocol => protocol.Service,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);

        ServiceDefinition[] filteredServices = services
            .Where(MatchesFilter)
            .OrderBy(service => service.Group)
            .ThenBy(service => service.Name)
            .ToArray();

        ServiceGroupView[] groups = filteredServices
            .GroupBy(service => service.Group)
            .Select(group => new ServiceGroupView(
                group.Key,
                group.Select(service => new ServiceItemView(
                        service,
                        FindContainer(service, containersByName),
                        FindProtocol(service, protocolsByService)))
                    .ToArray()))
            .ToArray();

        HashSet<string> cataloguedContainerNames = services
            .Where(service =>
                !string.IsNullOrWhiteSpace(service.ContainerName))
            .Select(service => service.ContainerName!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        HashSet<string> cataloguedProtocolKeys = services
            .Where(service =>
                !string.IsNullOrWhiteSpace(service.ProtocolServiceKey))
            .Select(service => service.ProtocolServiceKey!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        PublicContainerStatus[] uncataloguedContainers =
            (CurrentSnapshot?.Containers ?? [])
                .Where(container =>
                    !cataloguedContainerNames.Contains(container.Name))
                .ToArray();

        PublicProtocolStatus[] uncataloguedProtocols =
            (CurrentSnapshot?.Protocols ?? [])
                .Where(protocol =>
                    !cataloguedProtocolKeys.Contains(protocol.Service))
                .ToArray();

        int runningContainers = services.Count(service =>
            string.Equals(
                FindContainer(service, containersByName)?.State,
                "running",
                StringComparison.OrdinalIgnoreCase));

        return new ServicesView(
            services.Count,
            runningContainers,
            services.Count(service =>
                string.Equals(
                    service.Visibility,
                    "Public",
                    StringComparison.OrdinalIgnoreCase)),
            CurrentSnapshot?.Containers.Count ?? 0,
            CurrentSnapshot?.Protocols.Count(protocol => protocol.Succeeded) ?? 0,
            CurrentSnapshot?.Protocols.Count(protocol => !protocol.Succeeded) ?? 0,
            filteredServices.Length,
            uncataloguedContainers,
            uncataloguedProtocols,
            groups);
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;

        if (_catalogReload is not null)
        {
            await _catalogReload.DisposeAsync();
        }

        await _disposeSource.CancelAsync();

        if (_pollingTask is not null)
        {
            try
            {
                await _pollingTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _disposeSource.Dispose();
    }

    protected virtual Task DispatchCatalogUpdateAsync(
        Func<Task> update)
    {
        return InvokeAsync(update);
    }

    protected virtual void NotifyCatalogStateChanged()
    {
        StateHasChanged();
    }

    protected virtual Task DispatchComponentStateChangeAsync()
    {
        return InvokeAsync(StateHasChanged);
    }

    private bool MatchesFilter(ServiceDefinition service)
    {
        if (string.IsNullOrWhiteSpace(_filter))
        {
            return true;
        }

        string filter = _filter.Trim();

        return Contains(service.Name, filter) ||
               Contains(service.Group, filter) ||
               Contains(service.Summary, filter) ||
               Contains(service.Description, filter) ||
               Contains(service.ContainerName, filter) ||
               Contains(service.Protocol, filter) ||
               Contains(service.ProtocolServiceKey, filter) ||
               Contains(service.Endpoint, filter) ||
               Contains(service.Image, filter) ||
               service.SearchTerms.Any(term => Contains(term, filter));
    }

    private static PublicContainerStatus? FindContainer(
        ServiceDefinition service,
        IReadOnlyDictionary<string, PublicContainerStatus> containers)
    {
        return !string.IsNullOrWhiteSpace(service.ContainerName) &&
               containers.TryGetValue(service.ContainerName, out var container)
            ? container
            : null;
    }

    private static PublicProtocolStatus? FindProtocol(
        ServiceDefinition service,
        IReadOnlyDictionary<string, PublicProtocolStatus> protocols)
    {
        return !string.IsNullOrWhiteSpace(service.ProtocolServiceKey) &&
               protocols.TryGetValue(service.ProtocolServiceKey, out var protocol)
            ? protocol
            : null;
    }

    private static bool Contains(string? value, string filter)
    {
        return value?.Contains(
            filter,
            StringComparison.OrdinalIgnoreCase) == true;
    }

    private sealed record ServiceItemView(
        ServiceDefinition Service,
        PublicContainerStatus? Container,
        PublicProtocolStatus? Protocol);

    private sealed record ServiceGroupView(
        string Name,
        IReadOnlyList<ServiceItemView> Services);

    private sealed record ServicesView(
        int ConfiguredServices,
        int RunningContainers,
        int PublicServices,
        int SnapshotContainers,
        int SuccessfulProbes,
        int FailedProbes,
        int MatchingServices,
        IReadOnlyList<PublicContainerStatus> UncataloguedContainers,
        IReadOnlyList<PublicProtocolStatus> UncataloguedProtocols,
        IReadOnlyList<ServiceGroupView> Groups);
}
