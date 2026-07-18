namespace MissionControl.Dashboard.Refresh;

internal sealed class DashboardPollingLoop(
    TimeProvider timeProvider) :
    IDashboardPollingLoop
{
    public async Task RunAsync(
        TimeSpan interval,
        Func<CancellationToken, Task> onTick,
        CancellationToken cancellationToken)
    {
        using var timer =
            new PeriodicTimer(interval, timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(
                       cancellationToken))
            {
                await onTick(cancellationToken);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
