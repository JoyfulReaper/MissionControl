namespace MissionControl.Dashboard.Refresh;

internal sealed class RefreshGate
{
    private int _isRunning;

    public bool IsRunning =>
        Volatile.Read(ref _isRunning) != 0;

    public async Task<bool> TryRunAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(
                ref _isRunning,
                1,
                0) != 0)
        {
            return false;
        }

        try
        {
            await action(cancellationToken);
            return true;
        }
        finally
        {
            Volatile.Write(ref _isRunning, 0);
        }
    }
}