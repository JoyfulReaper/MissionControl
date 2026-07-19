using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using MissionControl.Contracts.Services;
using MissionControl.Dashboard.Agent;
using MissionControl.Dashboard.Configuration;
using MissionControl.Dashboard.Refresh;
using MissionControl.Dashboard.Services;
using MissionControl.UI.Services;

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

    private ServiceCatalogView CreateView()
    {
        return ServiceCatalogViewBuilder.Build(
            CurrentCatalog,
            CurrentSnapshot,
            _filter);
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