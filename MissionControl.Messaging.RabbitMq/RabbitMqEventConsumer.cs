using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MissionControl.Contracts;
using MissionControl.Observability.RabbitMq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text.Json;

namespace MissionControl.Messaging.RabbitMq;

public sealed class RabbitMqEventConsumer : BackgroundService, IAsyncDisposable, IRabbitMqConnectionStatus
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly RabbitMqOptions _connectionOptions;
    private readonly RabbitMqConsumerOptions _consumerOptions;
    private readonly ILogger<RabbitMqEventConsumer> _logger;
    private IConnection? _connection;
    private IChannel? _channel;
    private bool _disposed;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IIntegrationEventProcessor _processor;
    private string? _consumerTag;
    private static readonly TimeSpan RetryDelay =
        TimeSpan.FromSeconds(5);

    public RabbitMqEventConsumer(
        IOptions<RabbitMqOptions> connectionOptions,
        IOptions<RabbitMqConsumerOptions> consumerOptions,
        IIntegrationEventProcessor processor,
        ILogger<RabbitMqEventConsumer> logger)
    {
        _connectionOptions = connectionOptions.Value;
        _consumerOptions = consumerOptions.Value;
        _processor = processor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StartConsumingAsync(stoppingToken);
                break;
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "RabbitMQ consumer startup failed. Retrying in {RetryDelay}.",
                    RetryDelay);

                await Task.Delay(RetryDelay, stoppingToken);
            }
        }

        try
        {
            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                stoppingToken);
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task StartConsumingAsync(
        CancellationToken cancellationToken)
    {
        await EnsureConnectedAsync(cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(_channel!);

        consumer.ReceivedAsync += (_, delivery) =>
            HandleMessageAsync(delivery, cancellationToken);

        _consumerTag = await _channel!.BasicConsumeAsync(
            queue: _consumerOptions.QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Consuming events from queue {Queue}",
            _consumerOptions.QueueName);
    }

    private async Task HandleMessageAsync(
        BasicDeliverEventArgs delivery,
        CancellationToken cancellationToken)
    {
        var body = delivery.Body.ToArray();
        try
        {
            var integrationEvent =
            JsonSerializer.Deserialize<IntegrationEventEnvelope>(
                body,
                JsonOptions)
            ?? throw new JsonException(
                "Message deserialized to null.");

            await _processor.ProcessAsync(integrationEvent, cancellationToken);
            await _channel!.BasicAckAsync(
                deliveryTag: delivery.DeliveryTag,
                multiple: false,
                cancellationToken: cancellationToken
            );

            _logger.LogDebug(
                "Acknowledged event {EventId}",
                integrationEvent.EventId);
        }
        catch (JsonException exception)
        {
            _logger.LogError(
                exception,
                "Rejected malformed RabbitMQ message with delivery tag {DeliveryTag}",
                delivery.DeliveryTag);

            await _channel!.BasicRejectAsync(
                deliveryTag: delivery.DeliveryTag,
                requeue: false,
                cancellationToken: CancellationToken.None);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(
                exception,
                "Failed to process RabbitMQ message with delivery tag {DeliveryTag}",
                delivery.DeliveryTag);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to process RabbitMQ message with delivery tag {DeliveryTag}",
                delivery.DeliveryTag);

            // Temporary retry policy:
            // retry once, then discard until we add a dead-letter queue.
            await _channel!.BasicNackAsync(
                deliveryTag: delivery.DeliveryTag,
                multiple: false,
                requeue: !delivery.Redelivered,
                cancellationToken: CancellationToken.None);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
                "Stopping RabbitMQ event consumer");

        if (_channel?.IsOpen == true &&
            !string.IsNullOrWhiteSpace(_consumerTag))
        {
            try
            {
                await _channel.BasicCancelAsync(_consumerTag, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Error cancelling RabbitMQ consumer");
            }
        }

        await DisposeRabbitMqResourcesAsync();
        await base.StopAsync(cancellationToken);
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_connection?.IsOpen == true && _channel?.IsOpen == true)
            return;

        await DisposeRabbitMqResourcesAsync();

        var factory = new ConnectionFactory
        {
            HostName = _connectionOptions.HostName,
            Port = _connectionOptions.Port,
            UserName = _connectionOptions.UserName,
            Password = _connectionOptions.Password,
            VirtualHost = _connectionOptions.VirtualHost,
            ClientProvidedName =
            _connectionOptions.ClientProvidedName,

            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
        };

        _logger.LogInformation(
            "Connecting to RabbitMQ at {HostName}:{Port}, vhost {VirtualHost}",
            _connectionOptions.HostName,
            _connectionOptions.Port,
            _connectionOptions.VirtualHost);

        var connection = await factory.CreateConnectionAsync(cancellationToken);

        try
        {
            var channel = await connection.CreateChannelAsync(
                cancellationToken: cancellationToken);

            await channel.ExchangeDeclareAsync(
                exchange: _consumerOptions.ExchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken);

            await channel.QueueDeclareAsync(
                queue: _consumerOptions.QueueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null,
                cancellationToken: cancellationToken);

            await channel.QueueBindAsync(
                queue: _consumerOptions.QueueName,
                exchange: _consumerOptions.ExchangeName,
                routingKey: _consumerOptions.RoutingKey,
                arguments: null,
                cancellationToken: cancellationToken);

            await channel.BasicQosAsync(
                prefetchSize: 0,
                prefetchCount: _consumerOptions.PrefetchCount,
                global: false,
                cancellationToken: cancellationToken);

            _connection = connection;
            _channel = channel;

            _logger.LogInformation(
                "Connected to RabbitMQ and declared exchange {Exchange}",
                _consumerOptions.ExchangeName);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public RabbitMqConnectionSnapshot GetSnapshot()
    {
        var connection = _connection;
        var channel = _channel;

        return new RabbitMqConnectionSnapshot(
            ConnectionOpen: connection?.IsOpen == true,
            ChannelOpen: channel?.IsOpen == true);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await _gate.WaitAsync();

        try
        {
            if (_disposed)
                return;

            _disposed = true;
            await DisposeRabbitMqResourcesAsync();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private async ValueTask DisposeRabbitMqResourcesAsync()
    {
        if (_channel is not null)
        {
            try
            {
                await _channel.DisposeAsync();
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Error while disposing RabbitMQ channel");
            }
            _channel = null;
        }

        if (_connection is not null)
        {
            try
            {
                await _connection.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while disposing RabbitMQ connection");
            }
            _connection = null;
        }
    }
}