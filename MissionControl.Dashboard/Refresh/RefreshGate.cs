namespace MissionControl.Dashboard.Refresh;

internal sealed class RefreshGate
{
    private int isRunning;

    public bool IsRunning =>
        Volatile.Read(ref isRunning) != 0;

    public async Task<bool> TryRunAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(
                ref isRunning,
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
            Volatile.Write(ref isRunning, 0);
        }
    }
}
