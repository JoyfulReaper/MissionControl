namespace MissionControl.Dashboard.Refresh;

internal interface IDashboardPollingLoop
{
    Task RunAsync(
        TimeSpan interval,
        Func<CancellationToken, Task> onTick,
        CancellationToken cancellationToken);
}
