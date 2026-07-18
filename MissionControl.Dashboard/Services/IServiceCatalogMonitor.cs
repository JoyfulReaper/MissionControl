namespace MissionControl.Dashboard.Services;

internal interface IServiceCatalogMonitor
{
    IDisposable OnChange(
        Action<ServiceCatalogReloadCandidate> listener);
}

internal sealed record ServiceCatalogReloadCandidate(
    ServiceCatalogOptions? Options,
    bool BindingSucceeded);
