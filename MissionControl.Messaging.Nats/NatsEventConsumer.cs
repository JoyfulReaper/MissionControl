using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MissionControl.Contracts;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace MissionControl.Messaging.Nats;

public sealed class NatsEventConsumer(
    INatsJSContext jetStream,
    IOptions<NatsOptions> connectionOptions,
    IOptions<NatsConsumerOptions> consumerOptions,
    IIntegrationEventProcessor processor,
    NatsConsumerStatus status,
    ILogger<NatsEventConsumer> logger)
    : BackgroundService
{
    private readonly NatsOptions _connectionOptions = connectionOptions.Value;
    private readonly NatsConsumerOptions _consumerOptions = consumerOptions.Value;
    private static readonly TimeSpan ProcessingFailureRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MessageDispositionTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(stoppingToken);
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
                    "NATS consumer failed. Retrying in {RetryDelay}.",
                    RetryDelay);

                await Task.Delay(RetryDelay, stoppingToken);
            }
            finally
            {
                status.MarkStopped();
            }
        }
    }

    private async Task ConsumeAsync(
        CancellationToken cancellationToken)
    {
        var config = new ConsumerConfig
        {
            DurableName = _consumerOptions.DurableName,
            FilterSubject = _consumerOptions.FilterSubject,
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            MaxDeliver = _consumerOptions.MaxDeliveries
        };

        var consumer = await jetStream.CreateOrUpdateConsumerAsync(
            _connectionOptions.StreamName,
            config,
            cancellationToken);

        status.MarkRunning();

        logger.LogInformation(
            "Consuming NATS events from {Stream} as {Consumer} with filter {FilterSubject}",
            _connectionOptions.StreamName,
            _consumerOptions.DurableName,
            _consumerOptions.FilterSubject);

        await foreach (var message in
            consumer
                .ConsumeAsync<IntegrationEventEnvelope>(
                    cancellationToken: cancellationToken)
                .WithCancellation(cancellationToken))
        {
            await ProcessMessageAsync(
                message,
                cancellationToken);
        }
    }

    private async Task ProcessMessageAsync(
        INatsJSMsg<IntegrationEventEnvelope> message,
        CancellationToken cancellationToken)
    {
        if (message.Error is not null ||
            message.Data is null)
        {
            logger.LogError(
                message.Error,
                "Discarding malformed NATS message on subject {Subject}",
                message.Subject);

            using var dispositionCancellation = CreateDispositionCancellationSource(cancellationToken);
            await message.AckTerminateAsync(cancellationToken: dispositionCancellation.Token);

            return;
        }

        try
        {
            await processor.ProcessAsync(message.Data, cancellationToken);
            await message.AckAsync(cancellationToken: cancellationToken);

            logger.LogDebug(
                "Acknowledged event {EventId}",
                message.Data.EventId);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (PermanentIntegrationEventException exception)
        {
            logger.LogError(
                exception,
                "Terminating permanently unprocessable NATS event {EventId}",
                message.Data.EventId);

            await message.AckTerminateAsync(
                cancellationToken: CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to process NATS event {EventId}",
                message.Data.EventId);

            using var dispositionCancellation = CreateDispositionCancellationSource(cancellationToken);

            await message.NakAsync(
                new AckOpts
                {
                    NakDelay = ProcessingFailureRetryDelay
                },
                cancellationToken: dispositionCancellation.Token);
        }
    }

    private static CancellationTokenSource CreateDispositionCancellationSource(
    CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(MessageDispositionTimeout);
        return source;
    }
}