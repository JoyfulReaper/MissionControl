using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using MissionControl.Contracts.Agent;
using MissionControl.Contracts.Services;
using MissionControl.Dashboard.Agents;
using MissionControl.Dashboard.Configuration;
using MissionControl.Dashboard.Refresh;
using MissionControl.Dashboard.Services;
using MissionControl.UI.Services;

namespace MissionControl.Dashboard.Components.Pages;

public partial class Services : IAsyncDisposable
{
    private string? _filter;
    private string _selectedNodeId = "clanker";
    private AgentFleetRefreshController _agentRefresh = null!;
    private ServiceCatalogReloadController? _catalogReload;
    private readonly CancellationTokenSource _disposeSource = new();
    private Task? _pollingTask;
    private bool _isManualRefresh;
    private bool _disposed;

    private AgentNodeResult? CurrentNode =>
        AvailableNodes.FirstOrDefault(
            node =>
                string.Equals(
                    node.NodeId,
                    _selectedNodeId,
                    StringComparison.OrdinalIgnoreCase));

    private PublicNodeSnapshot? CurrentSnapshot =>
        CurrentNode?.Snapshot;

    internal IReadOnlyList<AgentNodeResult> AvailableNodes =>
        _agentRefresh?.CurrentNodes ?? [];

    internal PublicNodeSnapshot? SnapshotForTesting =>
        CurrentSnapshot;

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
    internal IAgentFleetClient AgentFleetClient { get; set; } = null!;

    [Inject]
    internal IOptions<DashboardRefreshOptions> RefreshOptions { get; set; } = null!;

    [Inject]
    internal TimeProvider TimeProvider { get; set; } = null!;

    [Inject]
    internal IDashboardPollingLoop PollingLoop { get; set; } = null!;

    [Inject]
    internal IOptions<ServiceCatalogOptions> CatalogOptions { get; set; } = null!;

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

        _agentRefresh = new AgentFleetRefreshController(
            AgentFleetClient,
            TimeProvider,
            TimeSpan.FromSeconds(
                RefreshOptions.Value.SnapshotStaleAfterSeconds));

        await _agentRefresh.RefreshAsync(_disposeSource.Token);

        EnsureSelectedNode();

        _pollingTask = PollingLoop.RunAsync(
            TimeSpan.FromSeconds(RefreshOptions.Value.AgentSnapshotRefreshSeconds),
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
            await _agentRefresh.RefreshAsync(_disposeSource.Token);

            EnsureSelectedNode();
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

        EnsureSelectedNode();

        if (!_disposed && !cancellationToken.IsCancellationRequested)
        {
            await DispatchComponentStateChangeAsync();
        }
    }

    private ServiceCatalogView CreateView()
    {
        return ServiceCatalogViewBuilder.BuildForNode(
            CurrentCatalog,
            _selectedNodeId,
            CurrentSnapshot,
            _filter);
    }

    private string? GetSnapshotError()
    {
        if (CurrentNode is not null)
        {
            return CurrentNode.Error ??
                _agentRefresh.RefreshWarning;
        }

        if (!string.IsNullOrWhiteSpace(_agentRefresh.RefreshWarning))
        {
            return _agentRefresh.RefreshWarning;
        }

        return _agentRefresh.IsInitialLoading
            ? null
            : $"{_selectedNodeId} is not available in host telemetry.";
    }

    private void EnsureSelectedNode()
    {
        if (AvailableNodes.Count == 0)
        {
            return;
        }

        bool selectedNodeExists =
            AvailableNodes.Any(
                node =>
                    string.Equals(
                        node.NodeId,
                        _selectedNodeId,
                        StringComparison.OrdinalIgnoreCase));

        if (!selectedNodeExists)
        {
            _selectedNodeId = AvailableNodes[0].NodeId;
        }
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
}