using Microsoft.Extensions.Options;

namespace MissionControl.Dashboard.Services;

internal sealed class ServiceCatalogReloadController : IAsyncDisposable
{
    public const string ReloadWarningMessage =
        "The service catalog could not be reloaded. Showing the last valid configuration.";

    private readonly IValidateOptions<ServiceCatalogOptions> _validator;
    private readonly ILogger<ServiceCatalogReloadController> _logger;
    private readonly Func<Func<Task>, Task> _dispatcher;
    private readonly Action _stateChanged;
    private readonly object _sync = new();
    private readonly IDisposable _subscription;
    private Task _pendingReloads = Task.CompletedTask;
    private bool _disposed;

    public ServiceCatalogReloadController(
        ServiceCatalogOptions initialOptions,
        IServiceCatalogMonitor monitor,
        IValidateOptions<ServiceCatalogOptions> validator,
        ILogger<ServiceCatalogReloadController> logger,
        Func<Func<Task>, Task> dispatcher,
        Action stateChanged)
    {
        ArgumentNullException.ThrowIfNull(initialOptions);
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(stateChanged);

        _validator = validator;
        _logger = logger;
        _dispatcher = dispatcher;
        _stateChanged = stateChanged;
        Services = initialOptions.Services.ToArray();
        _subscription = monitor.OnChange(QueueReload);
    }

    public IReadOnlyList<ServiceDefinition> Services { get; private set; }

    public string? ReloadWarning { get; private set; }

    private void QueueReload(ServiceCatalogReloadCandidate candidate)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _pendingReloads = ApplyAfterAsync(
                _pendingReloads,
                candidate);
        }
    }

    private async Task ApplyAfterAsync(
        Task previousReload,
        ServiceCatalogReloadCandidate candidate)
    {
        await Task.Yield();
        await previousReload;

        ValidateOptionsResult validation =
            candidate.BindingSucceeded && candidate.Options is not null
                ? _validator.Validate(null, candidate.Options)
                : ValidateOptionsResult.Fail(
                    "The service catalog could not be bound.");

        if (validation.Failed)
        {
            _logger.LogWarning(
                "Dashboard service catalog reload was rejected: {Failures}",
                string.Join(" ", validation.Failures));
        }

        try
        {
            await _dispatcher(
                () =>
                {
                    lock (_sync)
                    {
                        if (_disposed)
                        {
                            return Task.CompletedTask;
                        }

                        if (validation.Succeeded)
                        {
                            Services = candidate.Options!.Services.ToArray();
                            ReloadWarning = null;
                        }
                        else
                        {
                            ReloadWarning = ReloadWarningMessage;
                        }
                    }

                    _stateChanged();
                    return Task.CompletedTask;
                });
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Dashboard service catalog reload could not update the page.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task pendingReloads;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _subscription.Dispose();
            pendingReloads = _pendingReloads;
        }

        await pendingReloads;
    }
}
