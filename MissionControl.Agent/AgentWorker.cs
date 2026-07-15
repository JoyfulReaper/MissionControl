using Microsoft.Extensions.Options;

namespace MissionControl.Agent;

public sealed class AgentWorker(
    ILogger<AgentWorker> logger,
    IOptions<AgentOptions> options) : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        var agentOptions = options.Value;

        logger.LogInformation(
            "Mission Control Agent started for node {NodeName}.",
            agentOptions.NodeName);

        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(
                agentOptions.IntervalSeconds));

        do
        {
            logger.LogInformation(
                "Collecting agent snapshot for {NodeName}.",
                agentOptions.NodeName);
        }
        while (await timer.WaitForNextTickAsync(
                   stoppingToken));
    }
}