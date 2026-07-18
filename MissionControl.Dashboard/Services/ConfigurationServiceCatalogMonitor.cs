using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace MissionControl.Dashboard.Services;

internal sealed class ConfigurationServiceCatalogMonitor :
    IServiceCatalogMonitor,
    IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConfigurationServiceCatalogMonitor> _logger;
    private readonly object _sync = new();
    private readonly List<Action<ServiceCatalogReloadCandidate>> _listeners =
        [];
    private readonly IDisposable _reloadSubscription;
    private bool _disposed;

    public ConfigurationServiceCatalogMonitor(
        IConfiguration configuration,
        ILogger<ConfigurationServiceCatalogMonitor> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _reloadSubscription = ChangeToken.OnChange(
            configuration.GetReloadToken,
            Reload);
    }

    public IDisposable OnChange(
        Action<ServiceCatalogReloadCandidate> listener)
    {
        ArgumentNullException.ThrowIfNull(listener);

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _listeners.Add(listener);
        }

        return new ListenerSubscription(this, listener);
    }

    private void Reload()
    {
        ServiceCatalogReloadCandidate candidate;

        try
        {
            ServiceCatalogOptions? options = _configuration
                .GetRequiredSection(ServiceCatalogOptions.SectionName)
                .Get<ServiceCatalogOptions>();

            candidate = new ServiceCatalogReloadCandidate(
                options,
                BindingSucceeded: options is not null);
        }
        catch (Exception exception)
            when (exception is InvalidOperationException or
                FormatException)
        {
            _logger.LogWarning(
                exception,
                "The reloaded dashboard service catalog could not be bound.");
            candidate = new ServiceCatalogReloadCandidate(
                Options: null,
                BindingSucceeded: false);
        }

        Action<ServiceCatalogReloadCandidate>[] listeners;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            listeners = _listeners.ToArray();
        }

        foreach (Action<ServiceCatalogReloadCandidate> listener in listeners)
        {
            listener(candidate);
        }
    }

    private void RemoveListener(
        Action<ServiceCatalogReloadCandidate> listener)
    {
        lock (_sync)
        {
            _listeners.Remove(listener);
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _listeners.Clear();
        }

        _reloadSubscription.Dispose();
    }

    private sealed class ListenerSubscription(
        ConfigurationServiceCatalogMonitor owner,
        Action<ServiceCatalogReloadCandidate> listener) :
        IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.RemoveListener(listener);
        }
    }
}
