namespace MissionControl.Gateway.Messaging.RabbitMq;

public sealed class RabbitMqPublisherConnectionWorker(
    RabbitMqEventPublisher publisher,
    ILogger<RabbitMqPublisherConnectionWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan RetryDelay =
        TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await publisher.ConnectAsync(stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "RabbitMQ connection attempt failed. Retrying in {RetryDelay}.",
                    RetryDelay);
            }

            try
            {
                await Task.Delay(RetryDelay, stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }
}