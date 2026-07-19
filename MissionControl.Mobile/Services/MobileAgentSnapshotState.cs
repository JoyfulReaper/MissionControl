using MissionControl.Client.Agent;
using MissionControl.Contracts.Agent;

namespace MissionControl.Mobile.Services;

public sealed class MobileAgentSnapshotState(
    IAgentSnapshotClient agentClient) : IDisposable
{
    private readonly SemaphoreSlim _refreshGate =
        new(initialCount: 1, maxCount: 1);

    public event Action? Changed;

    public PublicNodeSnapshot? Snapshot { get; private set; }

    public bool IsInitialLoading { get; private set; } = true;

    public bool IsRefreshing { get; private set; }

    public bool IsManualRefreshing { get; private set; }

    public string? ErrorMessage { get; private set; }

    public Task EnsureLoadedAsync(
        CancellationToken cancellationToken = default)
    {
        return Snapshot is null
            ? RefreshAsync(
                isManualRefresh: false,
                cancellationToken)
            : Task.CompletedTask;
    }

    public async Task RefreshAsync(
        bool isManualRefresh,
        CancellationToken cancellationToken = default)
    {
        bool entered = await _refreshGate.WaitAsync(
            millisecondsTimeout: 0,
            cancellationToken);

        if (!entered)
        {
            return;
        }

        IsRefreshing = true;
        IsManualRefreshing = isManualRefresh;
        NotifyChanged();

        try
        {
            Snapshot =
                await agentClient.GetSnapshotAsync(
                    cancellationToken);

            ErrorMessage = null;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
            when (exception is
                HttpRequestException or
                TaskCanceledException or
                InvalidOperationException)
        {
            ErrorMessage =
                $"Latest Agent refresh failed: {exception.Message}";
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

    public void Dispose()
    {
        _refreshGate.Dispose();
    }

    private void NotifyChanged()
    {
        Changed?.Invoke();
    }
}