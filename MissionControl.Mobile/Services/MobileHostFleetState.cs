using MissionControl.Client.Infrastructure;

namespace MissionControl.Mobile.Services;

public sealed class MobileHostFleetState(
    IHostFleetClient hostFleetClient) : IDisposable
{
    private readonly SemaphoreSlim _refreshGate =
        new(initialCount: 1, maxCount: 1);

    public event Action? Changed;

    public IReadOnlyList<HostNodeSnapshot> Hosts { get; private set; } = [];

    public bool IsInitialLoading { get; private set; } = true;

    public bool IsRefreshing { get; private set; }

    public bool IsManualRefreshing { get; private set; }

    public string? ErrorMessage { get; private set; }

    public Task EnsureLoadedAsync(
        CancellationToken cancellationToken = default)
    {
        return IsInitialLoading
            ? RefreshAsync(
                isManualRefresh: false,
                cancellationToken)
            : Task.CompletedTask;
    }

    public async Task RefreshAsync(
        bool isManualRefresh,
        CancellationToken cancellationToken = default)
    {
        bool entered = await _refreshGate.WaitAsync(millisecondsTimeout: 0, cancellationToken);

        if (!entered)
        {
            return;
        }

        IsRefreshing = true;
        IsManualRefreshing = isManualRefresh;

        NotifyChanged();

        try
        {
            Hosts = await hostFleetClient.GetAsync(cancellationToken);

            ErrorMessage = null;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException exception)
        {
            ErrorMessage =
                exception.StatusCode ==
                System.Net.HttpStatusCode.Unauthorized
                    ? "Configure the Mobile API token in Settings to view hosts."
                    : "Host telemetry could not be retrieved.";
        }
        catch (TaskCanceledException)
        {
            ErrorMessage = "The host telemetry request timed out.";
        }
        catch (InvalidOperationException exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsInitialLoading = false;
            IsRefreshing = false;
            IsManualRefreshing = false;

            _refreshGate.Release();

            NotifyChanged();
        }
    }

    public async Task RunPollingAsync(
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(interval),
                "The polling interval must be greater than zero.");
        }

        using var timer =
            new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshAsync(isManualRefresh: false, cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public void Dispose()
    {
        _refreshGate.Dispose();
    }

    private void NotifyChanged()
    {
        Changed?.Invoke();
    }
}