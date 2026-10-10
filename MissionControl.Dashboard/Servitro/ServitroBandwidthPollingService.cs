using Microsoft.Extensions.Options;
using MissionControl.Dashboard.Refresh;

namespace MissionControl.Dashboard.Servitro;

internal sealed class ServitroBandwidthPollingService(
    ServitroBandwidthFleetRefreshController refreshController,
    IOptions<ServitroOptions> options,
    IDashboardPollingLoop pollingLoop) : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        await refreshController.RefreshAsync(stoppingToken);

        if (!options.Value.Enabled)
        {
            return;
        }

        await pollingLoop.RunAsync(
            TimeSpan.FromSeconds(options.Value.PollSeconds),
            async cancellationToken =>
            {
                await refreshController.RefreshAsync(cancellationToken);
            },
            stoppingToken);
    }
}
